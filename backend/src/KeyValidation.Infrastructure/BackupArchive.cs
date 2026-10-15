using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace KeyValidation.Infrastructure;

internal sealed record BackupManifest(int FormatVersion, Guid BackupId, DateTimeOffset CreatedAt, string[] Migrations, string ProtectedProbe, string ProbeSha256);
internal sealed record BackupContents(BackupManifest Manifest, string DumpPath, string KeysPath, string ConfigurationPath);

public sealed class BackupArchive(BackupSettings settings, IConfiguration configuration, IDataProtectionProvider protection)
{
    private static readonly byte[] Magic = "KGBK0001"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string ArchivePath(Guid id) => BackupPaths.Child(settings.Root, $"backup-{id:N}.kgb");

    internal async Task<(long Size, string Sha256)> Create(Guid id, DateTimeOffset createdAt, string dump, string work, string[] migrations, CancellationToken cancellationToken)
    {
        var probe = Guid.NewGuid().ToString("N");
        var manifest = new BackupManifest(1, id, createdAt, migrations, protection.CreateProtector("KeyValidation.Backup.Probe.v1").Protect(probe),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(probe))));
        var config = configuration.AsEnumerable().Where(x => x.Value is not null && (x.Key.StartsWith("Licensing:", StringComparison.OrdinalIgnoreCase) || x.Key.StartsWith("Offline:", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(x => x.Key, x => x.Value);
        using var plaintext = new BoundedArchiveBuffer(settings.MaxArchiveBytes - 60);
        using (var zip = new ZipArchive(plaintext, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddFile(zip, "database.dump", dump, cancellationToken);
            await AddBytes(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json), cancellationToken);
            await AddBytes(zip, "configuration.json", JsonSerializer.SerializeToUtf8Bytes(config, Json), cancellationToken);
            foreach (var file in KeyFiles(settings.KeysPath)) await AddFile(zip, "keys/" + Path.GetRelativePath(settings.KeysPath, file).Replace('\\', '/'), file, cancellationToken);
        }
        if (plaintext.Length + 60 > settings.MaxArchiveBytes) throw new BackupFailure("BACKUP_TOO_LARGE");
        var key = settings.EncryptionKey; var plain = plaintext.GetBuffer().AsSpan(0, checked((int)plaintext.Length));
        var encrypted = new byte[plain.Length]; var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        var header = new byte[32]; Magic.CopyTo(header, 0); id.TryWriteBytes(header.AsSpan(8, 16)); BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(24), plain.Length);
        try
        {
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, encrypted, tag, header);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        var temporary = BackupPaths.Child(work, "encrypted.tmp");
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(header, cancellationToken); await stream.WriteAsync(nonce, cancellationToken); await stream.WriteAsync(tag, cancellationToken);
            await stream.WriteAsync(encrypted, cancellationToken); await stream.FlushAsync(cancellationToken); stream.Flush(flushToDisk: true);
        }
        var destination = ArchivePath(id); File.Move(temporary, destination);
        await using var verification = File.OpenRead(destination);
        return (verification.Length, Convert.ToHexString(await SHA256.HashDataAsync(verification, cancellationToken)));
    }

    internal async Task<BackupContents> Extract(BackupJob job, string work, CancellationToken cancellationToken)
    {
        var path = ArchivePath(job.Id); BackupPaths.NoLinks(path);
        await using var source = File.OpenRead(path);
        if (source.Length < 60 || source.Length > settings.MaxArchiveBytes || source.Length > int.MaxValue) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
        var bytes = new byte[checked((int)source.Length)]; await source.ReadExactlyAsync(bytes, cancellationToken);
        if (source.ReadByte() != -1 || !HeaderMatches(bytes, job.Id) || BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(24, 8)) != bytes.Length - 60) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
        if (job.Sha256 is null || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), job.Sha256, StringComparison.Ordinal)) throw new BackupFailure("BACKUP_CHECKSUM_MISMATCH");
        var plaintext = new byte[bytes.Length - 60]; var key = settings.EncryptionKey;
        try
        {
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(bytes.AsSpan(32, 12), bytes.AsSpan(60), bytes.AsSpan(44, 16), plaintext, bytes.AsSpan(0, 32));
            using var stream = new MemoryStream(plaintext, writable: false); using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count is < 4 or > 10000) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                if (!SafeName(entry.FullName) || !names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
                expanded = checked(expanded + entry.Length);
                if (expanded > settings.MaxArchiveBytes) throw new BackupFailure("BACKUP_TOO_LARGE");
                var target = BackupPaths.Child(work, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(target)!); BackupPaths.NoLinks(target);
                await using var input = entry.Open(); await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await CopyBounded(input, output, entry.Length, cancellationToken);
            }
            if (!names.Contains("database.dump") || !names.Contains("manifest.json") || !names.Contains("configuration.json") || !names.Any(x => x.StartsWith("keys/", StringComparison.Ordinal))) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
            var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(BackupPaths.Child(work, "manifest.json"), cancellationToken), Json) ?? throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
            if (manifest.FormatVersion != 1 || manifest.BackupId != job.Id || manifest.Migrations.Length == 0) throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
            var contents = new BackupContents(manifest, BackupPaths.Child(work, "database.dump"), BackupPaths.Child(work, "keys"), BackupPaths.Child(work, "configuration.json"));
            await ValidateKeys(contents, cancellationToken); return contents;
        }
        catch (CryptographicException) { throw new BackupFailure("BACKUP_AUTHENTICATION_FAILED"); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal async Task<bool> Owned(BackupJob job, CancellationToken cancellationToken)
    {
        var path = ArchivePath(job.Id); if (!File.Exists(path)) return false;
        await using var input = File.OpenRead(path);
        if (input.Length != job.SizeBytes || input.Length < 60 || input.Length > settings.MaxArchiveBytes) return false;
        var header = new byte[32]; await input.ReadExactlyAsync(header, cancellationToken); if (!HeaderMatches(header, job.Id)) return false;
        input.Position = 0; return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)), job.Sha256, StringComparison.Ordinal);
    }

    private static bool HeaderMatches(byte[] bytes, Guid id) => bytes.Length >= 32 && bytes.AsSpan(0, 8).SequenceEqual(Magic) && new Guid(bytes.AsSpan(8, 16)) == id;
    private static bool SafeName(string name) => name.Length is > 0 and <= 512 && !name.Contains('\\') && !name.Contains(':') && !Path.IsPathRooted(name) &&
        name.Split('/').All(part => part is not "" and not "." and not "..") && (name is "database.dump" or "manifest.json" or "configuration.json" || name.StartsWith("keys/", StringComparison.Ordinal));
    private static IEnumerable<string> KeyFiles(string directory)
    {
        BackupPaths.NoLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            BackupPaths.NoLinks(entry);
            if (Directory.Exists(entry)) foreach (var file in KeyFiles(entry)) yield return file;
            else yield return entry;
        }
    }
    private async Task AddFile(ZipArchive archive, string name, string source, CancellationToken cancellationToken)
    {
        if (!SafeName(name)) throw new BackupFailure("BACKUP_PATH_UNSAFE"); BackupPaths.NoLinks(source);
        await using var input = File.OpenRead(source); if (input.Length > settings.MaxArchiveBytes) throw new BackupFailure("BACKUP_TOO_LARGE");
        await using var target = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); await CopyBounded(input, target, settings.MaxArchiveBytes, cancellationToken);
    }
    private static async Task AddBytes(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        try { await using var stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); await stream.WriteAsync(bytes, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static async Task CopyBounded(Stream source, Stream target, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536]; long copied = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) return;
                copied += read; if (copied > maximum) throw new BackupFailure("BACKUP_TOO_LARGE"); await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static async Task ValidateKeys(BackupContents contents, CancellationToken cancellationToken)
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string?>>(await File.ReadAllTextAsync(contents.ConfigurationPath, cancellationToken), Json) ?? throw new BackupFailure("BACKUP_KEYS_INVALID");
        try { _ = new SecretService(new ConfigurationBuilder().AddInMemoryCollection(config).Build()); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException) { throw new BackupFailure("BACKUP_KEYS_INVALID"); }
        var readable = DataProtectionProvider.Create(new DirectoryInfo(contents.KeysPath), builder => builder.SetApplicationName("KeyValidation"));
        using var cleanup = readable as IDisposable;
        var probe = readable.CreateProtector("KeyValidation.Backup.Probe.v1").Unprotect(contents.Manifest.ProtectedProbe);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(probe))), contents.Manifest.ProbeSha256, StringComparison.Ordinal)) throw new BackupFailure("BACKUP_KEYS_INVALID");
    }

    private sealed class BoundedArchiveBuffer(long maximum) : MemoryStream
    {
        private void Check(int count) { if (Position > maximum - count) throw new BackupFailure("BACKUP_TOO_LARGE"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { Check(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { Check(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        protected override void Dispose(bool disposing)
        {
            if (TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan());
            base.Dispose(disposing);
        }
    }
}
