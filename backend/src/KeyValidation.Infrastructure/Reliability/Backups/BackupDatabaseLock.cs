using System.Diagnostics;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KeyValidation.Infrastructure;
internal sealed class BackupDatabaseLock(NpgsqlConnection connection) : IAsyncDisposable
{
    public static async Task<BackupDatabaseLock> Acquire(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database")) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(1262836050, 3)", connection)
            {
                CommandTimeout = 0
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
