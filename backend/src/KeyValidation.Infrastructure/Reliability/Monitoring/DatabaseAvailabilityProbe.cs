using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KeyValidation.Infrastructure;
public sealed class DatabaseAvailabilityProbe(IConfiguration configuration) : IDatabaseAvailabilityProbe
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database"))
            {
                Pooling = false,
                Timeout = 3,
                CommandTimeout = 3
            };
            await using var connection = new NpgsqlConnection(cs.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception error)when (error is NpgsqlException or TimeoutException)
        {
            return false;
        }
    }
}
