using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KeyValidation.Infrastructure;
public sealed class BackupNativeProcess(BackupSettings settings, IConfiguration configuration)
{
    public Task DumpAsync(string path, CancellationToken cancellationToken) => Run("pg_dump", ["--format=custom", "--no-owner", "--no-acl", "--file", path], null, path, cancellationToken);
    public Task RestoreAsync(string path, string database, CancellationToken cancellationToken) => Run("pg_restore", ["--exit-on-error", "--no-owner", "--no-acl", "--dbname", database, path], database, null, cancellationToken);
    private async Task Run(string executable, string[] arguments, string? database, string? observedOutput, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database"));
        var start = new ProcessStartInfo(settings.Executable(executable))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("PG", StringComparison.Ordinal)).ToArray())
            start.Environment.Remove(key);
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString(CultureInfo.InvariantCulture);
        start.Environment["PGDATABASE"] = database ?? connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGCONNECT_TIMEOUT"] = "15";
        start.Environment["PGAPPNAME"] = "KeyValidationBackup";
        start.Environment["PGSSLMODE"] = connection.SslMode switch
        {
            SslMode.VerifyCA => "verify-ca",
            SslMode.VerifyFull => "verify-full",
            _ => connection.SslMode.ToString().ToLowerInvariant()};
        if (connection.TryGetValue("Root Certificate", out var certificate) && certificate is string { Length: > 0 } root)
            start.Environment["PGSSLROOTCERT"] = root;
        using var process = new Process
        {
            StartInfo = start
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        Task? drainOutput = null;
        Task? drainError = null;
        var started = false;
        try
        {
            if (!process.Start())
                throw new BackupFailure("POSTGRES_PROCESS_FAILED");
            started = true;
            drainOutput = Drain(process.StandardOutput);
            drainError = Drain(process.StandardError);
            var exited = process.WaitForExitAsync(deadline.Token);
            while (!exited.IsCompleted)
            {
                if (observedOutput is not null && File.Exists(observedOutput) && new FileInfo(observedOutput).Length > settings.MaxArchiveBytes)
                    throw new BackupFailure("BACKUP_TOO_LARGE");
                await Task.WhenAny(exited, Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token));
                deadline.Token.ThrowIfCancellationRequested();
            }

            await exited;
            if (process.ExitCode != 0)
                throw new BackupFailure(executable == "pg_dump" ? "PG_DUMP_FAILED" : "PG_RESTORE_FAILED");
            if (observedOutput is not null && new FileInfo(observedOutput).Length > settings.MaxArchiveBytes)
                throw new BackupFailure("BACKUP_TOO_LARGE");
        }
        catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackupFailure("POSTGRES_PROCESS_TIMEOUT");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            if (drainOutput is not null)
                await drainOutput;
            if (drainError is not null)
                await drainError;
            start.Environment.Remove("PGPASSWORD");
        }
    }

    private static async Task Drain(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer) > 0)
        {
        }

        Array.Clear(buffer);
    }
}
