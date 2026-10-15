using System.Diagnostics;
using System.Security.Cryptography;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Infrastructure;
// Recovery can run with the source database completely absent. Authenticity
// comes from the archive's AES-GCM tag and independently held encryption key.
public sealed class BackupRecoveryService(BackupSettings settings, BackupArchive archive, BackupDatabaseRestorer restorer)
{
    public async Task<IsolatedRestoreDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        settings.Require(forCreate: false);
        var start = Stopwatch.GetTimestamp();
        string? work = null;
        string? database = null;
        string? keys = null;
        var completed = false;
        try
        {
            var path = archive.ArchivePath(id);
            await using var input = File.OpenRead(path);
            if (input.Length < 60 || input.Length > settings.MaxArchiveBytes)
                throw new BackupFailure("BACKUP_ARCHIVE_INVALID");
            var metadata = new BackupJob
            {
                Id = id,
                Status = "succeeded",
                SizeBytes = input.Length,
                Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
            };
            work = BackupPaths.CreatePrivateDirectory(settings.Root, ".work-recovery-" + Guid.NewGuid().ToString("N"));
            var contents = await archive.Extract(metadata, work, cancellationToken);
            var restored = await restorer.RestoreAsync(contents, true, cancellationToken);
            database = restored.Database;
            keys = BackupPaths.CreatePrivateDirectory(settings.Root, "restored-keys-" + Guid.NewGuid().ToString("N"));
            Directory.Move(contents.KeysPath, BackupPaths.Child(keys, "data-protection"));
            File.Move(contents.ConfigurationPath, BackupPaths.Child(keys, "configuration.json"));
            BackupPaths.DeleteOwnedDirectory(settings.Root, work);
            work = null;
            completed = true;
            return new(id, database, keys, restored.Migrations, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        finally
        {
            try
            {
                if (work is not null)
                    BackupPaths.DeleteOwnedDirectory(settings.Root, work);
            }
            finally
            {
                if (!completed)
                {
                    try
                    {
                        if (database is not null)
                            await restorer.DropAsync(database, CancellationToken.None);
                    }
                    finally
                    {
                        if (keys is not null)
                            BackupPaths.DeleteOwnedDirectory(settings.Root, keys);
                    }
                }
            }
        }
    }
}
