using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KeyValidation.Infrastructure;

public sealed class BackupDatabaseRestorer(BackupNativeProcess native, IConfiguration configuration)
{
    internal async Task<(string Database, int Migrations)> RestoreAsync(BackupContents contents, bool keep, CancellationToken cancellationToken)
    {
        var name = "keyvalidation_restore_" + Guid.NewGuid().ToString("N"); var created = false; var succeeded = false;
        try
        {
            await using (var connection = AdministrativeConnection())
            {
                await connection.OpenAsync(cancellationToken);
                // Database lifecycle operations can include checkpoints and
                // filesystem cleanup, so use a bounded maintenance deadline.
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE template0", connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync(cancellationToken); created = true;
            }
            await native.RestoreAsync(contents.DumpPath, name, cancellationToken);
            var cs = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database")) { Database = name, Pooling = false };
            var migrations = new List<string>();
            await using (var restored = new NpgsqlConnection(cs.ConnectionString))
            {
                await restored.OpenAsync(cancellationToken);
                foreach (var table in new[] { "Applications", "Licenses", "AspNetUsers", "WebhookSubscriptions", "OperationsEvents", "__EFMigrationsHistory" })
                {
                    await using var check = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", restored); check.Parameters.AddWithValue("table", "public.\"" + table + "\"");
                    if (await check.ExecuteScalarAsync(cancellationToken) is not true) throw new BackupFailure("RESTORE_TABLES_MISSING");
                }
                await using var history = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", restored);
                await using var rows = await history.ExecuteReaderAsync(cancellationToken); while (await rows.ReadAsync(cancellationToken)) migrations.Add(rows.GetString(0));
            }
            if (!migrations.SequenceEqual(contents.Manifest.Migrations.Order(StringComparer.Ordinal))) throw new BackupFailure("RESTORE_MIGRATIONS_MISMATCH");
            succeeded = true; return (name, migrations.Count);
        }
        finally { if (created && (!keep || !succeeded)) await DropAsync(name, CancellationToken.None); }
    }

    internal async Task DropAsync(string name, CancellationToken cancellationToken)
    {
        if (!name.StartsWith("keyvalidation_restore_", StringComparison.Ordinal) || !Guid.TryParseExact(name[22..], "N", out _)) throw new BackupFailure("RESTORE_TARGET_INVALID");
        await using var connection = AdministrativeConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection) { CommandTimeout = 120 }; await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private NpgsqlConnection AdministrativeConnection() => new(new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database")) { Database = "postgres", Pooling = false }.ConnectionString);
}
