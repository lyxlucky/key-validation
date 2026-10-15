using System.Diagnostics;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KeyValidation.Infrastructure;
public sealed class BackupService(AppDbContext db, ApplicationAccessScope access, BackupSettings settings, BackupArchive archive, BackupNativeProcess native, BackupDatabaseRestorer restorer, BackupRecoveryService recovery, IConfiguration configuration, IServiceScopeFactory scopes, TimeProvider clock, ILogger<BackupService> logger)
{
    public async Task<BackupStatusDto> Status(CancellationToken cancellationToken = default)
    {
        access.Require(ApplicationPermissions.Administrator);
        var errors = settings.Errors();
        var successful = await db.BackupJobs.Where(x => x.Status == "succeeded" && x.DeletedAt == null).OrderByDescending(x => x.CompletedAt).Select(x => x.CompletedAt).FirstOrDefaultAsync(cancellationToken);
        var latest = await db.BackupJobs.OrderByDescending(x => x.CreatedAt).Select(x => (DateTimeOffset? )x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var ready = settings.Enabled && errors.Length == 0;
        return new(settings.Enabled, errors.Length == 0, ready, settings.IntervalHours, settings.RetentionCount, settings.MaxArchiveBytes, errors, successful, ready ? latest?.AddHours(settings.IntervalHours) ?? clock.GetUtcNow() : null);
    }

    public async Task<PagedResult<BackupJobDto>> List(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        access.Require(ApplicationPermissions.Administrator);
        ReliabilityRules.Page(page, pageSize);
        var query = db.BackupJobs.AsNoTracking().Where(x => x.DeletedAt == null);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new(items.Select(Dto).ToArray(), count, page, pageSize);
    }

    public async Task<BackupJobDto> CreateAsync(Actor? actor = null, CancellationToken cancellationToken = default)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        await using var operation = await BackupDatabaseLock.Acquire(configuration, cancellationToken);
        return await CreateUnderLock(actor, cancellationToken);
    }

    public async Task<BackupJobDto?> RunScheduledAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return null;
        access.RequireWrite(ApplicationPermissions.Administrator);
        await using var operation = await BackupDatabaseLock.Acquire(configuration, cancellationToken);
        var latest = await db.BackupJobs.OrderByDescending(x => x.CreatedAt).Select(x => (DateTimeOffset? )x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (latest?.AddHours(settings.IntervalHours) > clock.GetUtcNow())
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await access.RecheckWrite(ApplicationPermissions.Administrator, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        return await CreateUnderLock(null, cancellationToken);
    }

    private async Task<BackupJobDto> CreateUnderLock(Actor? actor, CancellationToken cancellationToken)
    {
        var job = new BackupJob
        {
            CreatedAt = clock.GetUtcNow()
        };
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await access.RecheckWrite(ApplicationPermissions.Administrator, cancellationToken);
            await RecoverInterrupted(cancellationToken);
            db.BackupJobs.Add(job);
            if (actor is not null)
                ReliabilityRules.Audit(db, actor, "backup.create", job.Id, null, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // Execution state belongs to the durable job. It must still be recorded
        // when the initiating request disconnects or its account later changes.
        using var executionScope = scopes.CreateScope();
        var state = executionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await state.BackupJobs.SingleAsync(x => x.Id == job.Id, CancellationToken.None);
        string? work = null;
        try
        {
            settings.Require();
            EnsureRoot();
            work = BackupPaths.CreatePrivateDirectory(settings.Root, ".work-" + job.Id.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
            var dump = BackupPaths.Child(work, "database.dump");
            var migrations = (await state.Database.GetAppliedMigrationsAsync(cancellationToken)).Order(StringComparer.Ordinal).ToArray();
            await native.DumpAsync(dump, cancellationToken);
            var created = await archive.Create(record.Id, record.CreatedAt, dump, work, migrations, cancellationToken);
            record.SizeBytes = created.Size;
            record.Sha256 = created.Sha256;
            record.Status = "succeeded";
        }
        catch (Exception error)
        {
            record.Status = "failed";
            record.ErrorCode = ErrorCode(error);
            logger.LogWarning("Backup creation failed: {ErrorType}, {ErrorCode}", error.GetType().Name, record.ErrorCode);
        }
        finally
        {
            if (work is not null)
                try
                {
                    BackupPaths.DeleteOwnedDirectory(settings.Root, work);
                }
                catch (Exception error)when (error is IOException or UnauthorizedAccessException or BackupFailure)
                {
                    record.Status = "failed";
                    record.ErrorCode = "PLAINTEXT_CLEANUP_FAILED";
                }
        }

        record.CompletedAt = clock.GetUtcNow();
        await state.SaveChangesAsync(CancellationToken.None);
        if (record.Status == "succeeded")
        {
            try
            {
                await Retain(state, CancellationToken.None);
            }
            catch (Exception error)when (error is IOException or UnauthorizedAccessException or BackupFailure)
            {
                record.ErrorCode = "RETENTION_FAILED";
                await state.SaveChangesAsync(CancellationToken.None);
            }
        }

        return Dto(record);
    }

    public async Task<BackupVerificationDto> VerifyAsync(Guid id, Actor? actor = null, CancellationToken cancellationToken = default)
    {
        var result = await Restore(id, false, actor, cancellationToken);
        return new(id, result.Error is null ? "succeeded" : "failed", result.CompletedAt, result.Duration, result.Migrations, result.Error is null, result.Error);
    }

    public async Task<IsolatedRestoreDto> RestoreIsolatedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        access.Require(ApplicationPermissions.Administrator);
        return await recovery.RestoreAsync(id, cancellationToken);
    }

    private sealed record RestoreResult(string? Error, string? Database, string? Keys, int Migrations, long Duration, DateTimeOffset CompletedAt);
    private async Task<RestoreResult> Restore(Guid id, bool keep, Actor? actor, CancellationToken cancellationToken)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        await using var operation = await BackupDatabaseLock.Acquire(configuration, cancellationToken);
        var job = await db.BackupJobs.SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, cancellationToken) ?? throw new RuleException("NOT_FOUND", 404);
        if (job.Status != "succeeded")
            throw new RuleException("BACKUP_NOT_AVAILABLE", 409);
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await access.RecheckWrite(ApplicationPermissions.Administrator, cancellationToken);
            await RecoverInterrupted(cancellationToken);
            job.VerificationStatus = "running";
            job.VerificationErrorCode = null;
            if (actor is not null)
                ReliabilityRules.Audit(db, actor, "backup.verify", id, null, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        var start = Stopwatch.GetTimestamp();
        string? errorCode = null;
        string? work = null;
        string? database = null;
        string? keys = null;
        var migrations = 0;
        var stage = "configuration";
        try
        {
            settings.Require(forCreate: false);
            EnsureRoot();
            work = BackupPaths.CreatePrivateDirectory(settings.Root, ".work-" + id.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
            stage = "archive";
            var contents = await archive.Extract(job, work, cancellationToken);
            stage = "database";
            var restored = await restorer.RestoreAsync(contents, keep, cancellationToken);
            database = restored.Database;
            migrations = restored.Migrations;
            if (keep)
            {
                keys = BackupPaths.CreatePrivateDirectory(settings.Root, "restored-keys-" + Guid.NewGuid().ToString("N"));
                Directory.Move(contents.KeysPath, BackupPaths.Child(keys, "data-protection"));
                File.Move(contents.ConfigurationPath, BackupPaths.Child(keys, "configuration.json"));
            }
        }
        catch (Exception error)
        {
            errorCode = ErrorCode(error);
            logger.LogWarning("Backup restore failed at {Stage}: {ErrorType}, {ErrorCode}", stage, error.GetType().Name, errorCode);
        }
        finally
        {
            if (work is not null)
                try
                {
                    BackupPaths.DeleteOwnedDirectory(settings.Root, work);
                }
                catch (Exception error)when (error is IOException or UnauthorizedAccessException or BackupFailure)
                {
                    errorCode = "PLAINTEXT_CLEANUP_FAILED";
                }

            if (keep && errorCode is not null)
            {
                if (database is not null)
                    await restorer.DropAsync(database, CancellationToken.None);
                if (keys is not null)
                    BackupPaths.DeleteOwnedDirectory(settings.Root, keys);
            }
        }

        var duration = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var completed = clock.GetUtcNow();
        using var scope = scopes.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await state.BackupJobs.SingleAsync(x => x.Id == id, CancellationToken.None);
        record.VerificationStatus = errorCode is null ? "succeeded" : "failed";
        record.VerificationErrorCode = errorCode;
        record.VerificationDurationMilliseconds = duration;
        record.VerifiedAt = errorCode is null ? completed : null;
        await state.SaveChangesAsync(CancellationToken.None);
        return new(errorCode, database, keys, migrations, duration, completed);
    }

    private void EnsureRoot()
    {
        BackupPaths.NoLinks(settings.Root);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(settings.Root);
        else
            Directory.CreateDirectory(settings.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private async Task RecoverInterrupted(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        await state.BackupJobs.Where(x => x.Status == "running").ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "failed").SetProperty(x => x.ErrorCode, "BACKUP_INTERRUPTED").SetProperty(x => x.CompletedAt, now), cancellationToken);
        await state.BackupJobs.Where(x => x.VerificationStatus == "running").ExecuteUpdateAsync(update => update.SetProperty(x => x.VerificationStatus, "failed").SetProperty(x => x.VerificationErrorCode, "RESTORE_INTERRUPTED"), cancellationToken);
    }

    private async Task Retain(AppDbContext state, CancellationToken cancellationToken)
    {
        var obsolete = await state.BackupJobs.Where(x => x.Status == "succeeded" && x.DeletedAt == null).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(settings.RetentionCount).ToArrayAsync(cancellationToken);
        foreach (var item in obsolete)
        {
            if (!await archive.Owned(item, cancellationToken))
                continue;
            File.Delete(archive.ArchivePath(item.Id));
            item.DeletedAt = clock.GetUtcNow();
        }

        await state.SaveChangesAsync(cancellationToken);
    }

    private static string ErrorCode(Exception error) => error switch
    {
        BackupFailure failure => failure.Code,
        OperationCanceledException => "BACKUP_CANCELLED",
        UnauthorizedAccessException => "BACKUP_ACCESS_DENIED",
        FileNotFoundException or DirectoryNotFoundException => "BACKUP_FILE_MISSING",
        IOException => "BACKUP_IO_FAILED",
        NpgsqlException => "BACKUP_DATABASE_FAILED",
        System.Security.Cryptography.CryptographicException => "BACKUP_AUTHENTICATION_FAILED",
        System.Text.Json.JsonException or InvalidDataException => "BACKUP_ARCHIVE_INVALID",
        _ => "BACKUP_OPERATION_FAILED"
    };
    private static BackupJobDto Dto(BackupJob x) => new(x.Id, x.Status, x.CreatedAt, x.CompletedAt, x.SizeBytes, x.Sha256, x.VerifiedAt, x.VerificationStatus, x.VerificationDurationMilliseconds, x.ErrorCode, x.VerificationErrorCode);
}
