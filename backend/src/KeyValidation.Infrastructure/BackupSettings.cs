using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace KeyValidation.Infrastructure;

public sealed class BackupFailure(string code) : Exception(code) { public string Code { get; } = code; }

public sealed class BackupSettings(IConfiguration configuration)
{
    public bool Enabled => bool.TryParse(configuration["Backup:Enabled"], out var value) && value;
    public int IntervalHours => Integer("IntervalHours", 24);
    public int RetentionCount => Integer("RetentionCount", 7);
    public long MaxArchiveBytes => long.TryParse(configuration["Backup:MaxArchiveBytes"], out var value) ? value : 268435456;
    public string Root => Path.GetFullPath(configuration["Backup:Path"] ?? throw new BackupFailure("BACKUP_PATH_UNCONFIGURED"));
    public string KeysPath => Path.GetFullPath(configuration["DataProtection:Path"] ?? throw new BackupFailure("DATA_PROTECTION_PATH_UNCONFIGURED"));
    public string Executable(string name) => Path.Combine(configuration["Backup:PostgresBinPath"] ?? "", name + (OperatingSystem.IsWindows() ? ".exe" : ""));
    public byte[] EncryptionKey => Convert.FromBase64String(configuration["Backup:EncryptionKey"] ?? "");

    public string[] Errors(bool forCreate = true)
    {
        var errors = new List<string>();
        var path = configuration["Backup:Path"];
        string? backupRoot = null;
        if (string.IsNullOrWhiteSpace(path)) errors.Add("BACKUP_PATH_UNCONFIGURED");
        else
            try
            {
                if (!Path.IsPathFullyQualified(path)) errors.Add("BACKUP_PATH_INVALID");
                else { BackupPaths.NoLinks(Root); backupRoot = Path.TrimEndingDirectorySeparator(Root); }
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or BackupFailure) { errors.Add("BACKUP_PATH_UNSAFE"); }
        try
        {
            var key = EncryptionKey;
            try { if (key.Length != 32) errors.Add("BACKUP_ENCRYPTION_KEY_INVALID"); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (FormatException) { errors.Add("BACKUP_ENCRYPTION_KEY_INVALID"); }
        var bin = configuration["Backup:PostgresBinPath"];
        if (string.IsNullOrWhiteSpace(bin) || !Path.IsPathFullyQualified(bin) || !File.Exists(Executable("pg_dump")) || !File.Exists(Executable("pg_restore"))) errors.Add("POSTGRES_TOOLS_UNAVAILABLE");
        if (IntervalHours is < 1 or > 8760 || RetentionCount is < 1 or > 365 || MaxArchiveBytes is < 1024 or > 1073741824 ||
            configuration["Backup:IntervalHours"] is { Length: > 0 } interval && !int.TryParse(interval, out _) ||
            configuration["Backup:RetentionCount"] is { Length: > 0 } retention && !int.TryParse(retention, out _) ||
            configuration["Backup:MaxArchiveBytes"] is { Length: > 0 } maximum && !long.TryParse(maximum, out _) ||
            configuration["Backup:Enabled"] is { Length: > 0 } enabled && !bool.TryParse(enabled, out _)) errors.Add("BACKUP_CONFIGURATION_INVALID");
        if (forCreate)
        {
            try
            {
                var keysRoot = Path.TrimEndingDirectorySeparator(KeysPath);
                if (!Directory.Exists(keysRoot)) errors.Add("DATA_PROTECTION_PATH_UNAVAILABLE");
                else BackupPaths.NoLinks(keysRoot);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var keysPrefix = Path.EndsInDirectorySeparator(keysRoot) ? keysRoot : keysRoot + Path.DirectorySeparatorChar;
                if (backupRoot is not null && (backupRoot.Equals(keysRoot, comparison) || backupRoot.StartsWith(keysPrefix, comparison))) errors.Add("BACKUP_PATH_UNSAFE");
            }
            catch (Exception error) when (error is BackupFailure or ArgumentException or IOException or UnauthorizedAccessException) { errors.Add("DATA_PROTECTION_PATH_UNAVAILABLE"); }
            if (string.IsNullOrWhiteSpace(configuration["Licensing:CurrentKeyVersion"]) || !configuration.GetSection("Licensing:Keys").GetChildren().Any()) errors.Add("LICENSING_CONFIGURATION_UNAVAILABLE");
        }
        return errors.Distinct().ToArray();
    }
    public void Require(bool forCreate = true) { var errors = Errors(forCreate); if (errors.Length > 0) throw new BackupFailure(errors[0]); }
    private int Integer(string name, int fallback) => int.TryParse(configuration["Backup:" + name], out var value) ? value : fallback;
}

internal static class BackupPaths
{
    public static void NoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new BackupFailure("BACKUP_PATH_UNSAFE");
            var parent = Path.GetDirectoryName(current); if (parent == current) break; current = parent!;
        }
    }
    public static string Child(string root, string name)
    {
        NoLinks(root); var absolute = Path.GetFullPath(Path.Combine(root, name));
        if (!absolute.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new BackupFailure("BACKUP_PATH_UNSAFE");
        NoLinks(absolute); return absolute;
    }
    public static string CreatePrivateDirectory(string root, string name)
    {
        var path = Child(root, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new BackupFailure("BACKUP_PATH_CONFLICT");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    public static void DeleteOwnedDirectory(string root, string path)
    {
        var absolute = Child(root, Path.GetRelativePath(root, path));
        if (!Directory.Exists(absolute)) return;
        // Enumerate manually and never traverse a reparse point.
        foreach (var file in Directory.EnumerateFileSystemEntries(absolute))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.Directory) == 0) File.Delete(file);
            else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(file);
            else DeleteOwnedDirectory(absolute, file);
        }
        Directory.Delete(absolute);
    }
}
