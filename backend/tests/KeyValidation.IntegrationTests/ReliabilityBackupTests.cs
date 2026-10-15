using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using KeyValidation.Application;
using KeyValidation.Api;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Npgsql;
using System.Text.Json;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityBackupTests(ApiFixture fixture, ITestOutputHelper output) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task NativeDumpAndIsolatedRestoreVerifyRealDatabaseAndRejectTampering()
    {
        using var directory = new BackupTestDirectory();
        await using var host = Host(directory.Path); using var admin = await Login(host);
        var (baseAdmin, app, plan) = await fixture.Catalog(); using var baseCleanup = baseAdmin;
        var issued = await ApiFixture.Issue(baseAdmin, app, plan);
        var status = await admin.GetAsync("/api/admin/backups/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.True((await status.Content.ReadFromJsonAsync<BackupStatusDto>())!.Configured);
        var response = await admin.PostAsync("/api/admin/backups", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<BackupJobDto>())!;
        Assert.True(job.Status == "succeeded", job.ErrorCode); Assert.True(job.SizeBytes > 0); Assert.Equal(64, job.Sha256!.Length);
        var verify = await admin.PostAsync($"/api/admin/backups/{job.Id}/verify", null);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var restored = (await verify.Content.ReadFromJsonAsync<BackupVerificationDto>())!;
        Assert.True(restored.Status == "succeeded", restored.ErrorCode); Assert.True(restored.KeysReadable); Assert.True(restored.MigrationCount >= 5); Assert.True(restored.DurationMilliseconds > 0);
        output.WriteLine($"Native pg_dump/pg_restore verification: archive={job.SizeBytes} bytes; migrations={restored.MigrationCount}; restore={restored.DurationMilliseconds} ms; original license remains readable.");
        Assert.Equal(HttpStatusCode.OK, (await baseAdmin.GetAsync($"/api/admin/licenses/{issued.Licenses[0].Id}")).StatusCode);
        var archive = System.IO.Path.Combine(directory.Path, $"backup-{job.Id:N}.kgb");
        var original = await File.ReadAllBytesAsync(archive);
        original[^1] ^= 1; await File.WriteAllBytesAsync(archive, original);
        var corrupted = (await (await admin.PostAsync($"/api/admin/backups/{job.Id}/verify", null)).Content.ReadFromJsonAsync<BackupVerificationDto>())!;
        Assert.Equal("failed", corrupted.Status); Assert.NotNull(corrupted.ErrorCode);
        using (var state = fixture.Services.CreateScope())
            await state.ServiceProvider.GetRequiredService<AppDbContext>().BackupJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.Sha256, Convert.ToHexString(SHA256.HashData(original))));
        var authenticated = (await (await admin.PostAsync($"/api/admin/backups/{job.Id}/verify", null)).Content.ReadFromJsonAsync<BackupVerificationDto>())!;
        Assert.Equal("BACKUP_AUTHENTICATION_FAILED", authenticated.ErrorCode);
        await File.WriteAllBytesAsync(archive, original[..40]);
        var truncated = (await (await admin.PostAsync($"/api/admin/backups/{job.Id}/verify", null)).Content.ReadFromJsonAsync<BackupVerificationDto>())!;
        Assert.Equal("failed", truncated.Status);
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".work-*"));
    }

    [Fact]
    public async Task RetentionDeletesOnlyOwnedSuccessfulArchivesAndSizeFailureCleansPlaintext()
    {
        using var directory = new BackupTestDirectory();
        await using var host = Host(directory.Path, new Dictionary<string, string?> { ["Backup:RetentionCount"] = "2" }); using var admin = await Login(host);
        Directory.CreateDirectory(directory.Path); var unrelated = System.IO.Path.Combine(directory.Path, "operator-notes.txt"); await File.WriteAllTextAsync(unrelated, "keep me");
        for (var i = 0; i < 3; i++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            var response = await admin.PostAsync("/api/admin/backups", null); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("succeeded", (await response.Content.ReadFromJsonAsync<BackupJobDto>())!.Status);
        }
        Assert.Equal(2, Directory.EnumerateFiles(directory.Path, "backup-*.kgb").Count()); Assert.True(File.Exists(unrelated));
        await using var limited = Host(directory.Path, new Dictionary<string, string?> { ["Backup:MaxArchiveBytes"] = "1024" }); using var limitedAdmin = await Login(limited);
        var failed = await limitedAdmin.PostAsync("/api/admin/backups", null); Assert.Equal(HttpStatusCode.Created, failed.StatusCode);
        var job = (await failed.Content.ReadFromJsonAsync<BackupJobDto>())!; Assert.Equal("failed", job.Status); Assert.Equal("BACKUP_TOO_LARGE", job.ErrorCode);
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".work-*"));
    }

    private WebApplicationFactory<Program> Host(string path, Dictionary<string, string?>? overrides = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Backup:Path"] = path, ["Backup:EncryptionKey"] = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path))),
            ["Backup:PostgresBinPath"] = System.IO.Path.Combine(BackupTestDirectory.Workspace, ".local", "pgsql-17.10-client", "bin"), ["Operations:WorkersEnabled"] = "false"
        };
        config["Licensing:Keys:v1"] = fixture.Services.GetRequiredService<IConfiguration>()["Licensing:Keys:v1"];
        if (overrides is not null) foreach (var pair in overrides) config[pair.Key] = pair.Value;
        return fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(config)));
    }
    private async Task<HttpClient> Login(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
        await ApiFixture.Csrf(client); return client;
    }

    [Fact]
    public async Task ScheduleSerializesConcurrentInstancesAndWaitsUntilTheConfiguredNextDay()
    {
        using var directory = new BackupTestDirectory(); fixture.Clock.Advance(TimeSpan.FromDays(1));
        await using var host = Host(directory.Path, new Dictionary<string, string?> { ["Backup:Enabled"] = "true" });
        using var admin = await Login(host);
        using var first = host.Services.CreateScope(); using var second = host.Services.CreateScope();
        var outcomes = await Task.WhenAll(first.ServiceProvider.GetRequiredService<BackupService>().RunScheduledAsync(), second.ServiceProvider.GetRequiredService<BackupService>().RunScheduledAsync());
        Assert.Single(outcomes, x => x is not null); Assert.Equal("succeeded", outcomes.Single(x => x is not null)!.Status);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        using var early = host.Services.CreateScope(); Assert.Null(await early.ServiceProvider.GetRequiredService<BackupService>().RunScheduledAsync());
        fixture.Clock.Advance(TimeSpan.FromHours(23));
        using var due = host.Services.CreateScope(); Assert.Equal("succeeded", (await due.ServiceProvider.GetRequiredService<BackupService>().RunScheduledAsync())!.Status);
    }

    [Theory]
    [InlineData("keys", true)]
    [InlineData("keys/backups", true)]
    [InlineData("keys-backups", false)]
    [InlineData(".", false)]
    public void BackupDirectoryCannotBeNestedInsideTheKeyMaterialItArchives(string backupPath, bool unsafePath)
    {
        using var directory = new BackupTestDirectory();
        var keys = System.IO.Path.Combine(directory.Path, "keys"); Directory.CreateDirectory(keys);
        var config = fixture.Services.GetRequiredService<IConfiguration>().AsEnumerable().ToDictionary(x => x.Key, x => x.Value);
        config["Backup:Path"] = System.IO.Path.Combine(directory.Path, backupPath); config["DataProtection:Path"] = keys;
        config["Backup:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        config["Backup:PostgresBinPath"] = System.IO.Path.Combine(BackupTestDirectory.Workspace, ".local", "pgsql-17.10-client", "bin");
        var settings = new BackupSettings(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        Assert.Equal(unsafePath, settings.Errors().Contains("BACKUP_PATH_UNSAFE"));
    }

    [Fact]
    public async Task RecoveryCliWorksWhenTheOriginalDatabaseIsUnavailable()
    {
        using var directory = new BackupTestDirectory();
        using var offlineKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var host = Host(directory.Path, new Dictionary<string, string?> { ["Offline:CurrentKeyId"] = "backup-test", ["Offline:Keys:backup-test"] = offlineKey.ExportPkcs8PrivateKeyPem(), ["Offline:Enabled"] = "true" }); using var admin = await Login(host);
        var (baseAdmin, app, plan) = await fixture.Catalog(); using var cleanup = baseAdmin;
        var issued = await ApiFixture.Issue(baseAdmin, app, plan);
        var created = (await (await admin.PostAsync("/api/admin/backups", null)).Content.ReadFromJsonAsync<BackupJobDto>())!;
        Assert.Equal("succeeded", created.Status);
        var original = fixture.Services.GetRequiredService<IConfiguration>().GetConnectionString("Database")!;
        var unavailable = new NpgsqlConnectionStringBuilder(original) { Database = "missing_recovery_" + Guid.NewGuid().ToString("N") };
        await using var recovery = Host(directory.Path, new Dictionary<string, string?> { ["ConnectionStrings:Database"] = unavailable.ConnectionString });
        using var standard = new StringWriter(); using var errors = new StringWriter(); IsolatedRestoreDto? restored = null;
        try
        {
            // A disaster recovery command must not need the lost BackupJobs table.
            Assert.Equal(0, await BackupCli.Run(recovery.Services, ["backup", "restore", created.Id.ToString()], standard, errors));
            restored = JsonSerializer.Deserialize<IsolatedRestoreDto>(standard.ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.StartsWith("keyvalidation_restore_", restored.DatabaseName); Assert.NotEqual(unavailable.Database, restored.DatabaseName);
            var cs = new NpgsqlConnectionStringBuilder(original) { Database = restored.DatabaseName, Pooling = false };
            await using (var connection = new NpgsqlConnection(cs.ConnectionString))
            {
                await connection.OpenAsync(); await using var command = new NpgsqlCommand("SELECT \"KeyDigest\" FROM \"t_licenses\" WHERE \"Id\" = @id", connection); command.Parameters.AddWithValue("id", issued.Licenses[0].Id);
                var digest = (string?)await command.ExecuteScalarAsync(); Assert.NotNull(digest);
                var config = JsonSerializer.Deserialize<Dictionary<string, string?>>(await File.ReadAllTextAsync(System.IO.Path.Combine(restored.KeysDirectory, "configuration.json")))!;
                var secrets = new SecretService(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
                Assert.Equal(digest, secrets.DigestLicense(issued.Licenses[0].Key).Digest);
                var restoredSigner = new OfflineLicenseKeyring(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
                Assert.True(restoredSigner.Configuration().Configured); Assert.Equal("backup-test", restoredSigner.Configuration().CurrentKeyId);
                var now = fixture.Clock.GetUtcNow(); var certificate = restoredSigner.Sign(new OfflineLicensePayload(1, Guid.NewGuid(), "recovery", Guid.NewGuid(), Guid.NewGuid(), ["basic"], now, now, now.AddHours(1), new string('A', 64)));
                var parts = certificate.Content.Split('.');
                var signature = Convert.FromBase64String(parts[2].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[2].Length % 4) % 4));
                Assert.True(offlineKey.VerifyData(System.Text.Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            }
            Assert.NotEmpty(Directory.EnumerateFiles(System.IO.Path.Combine(restored.KeysDirectory, "data-protection"), "*.xml"));
            output.WriteLine($"CLI disaster recovery with unavailable original database: migrations={restored.MigrationCount}; restore={restored.DurationMilliseconds} ms; restored license digest matches exported key material.");
        }
        finally
        {
            if (restored is not null)
            {
                Assert.StartsWith("keyvalidation_restore_", restored.DatabaseName); Assert.True(Guid.TryParseExact(restored.DatabaseName[22..], "N", out _));
                var adminConnection = new NpgsqlConnectionStringBuilder(original) { Database = "postgres", Pooling = false };
                await using var connection = new NpgsqlConnection(adminConnection.ConnectionString); await connection.OpenAsync();
                await using var cleanupDatabase = new NpgsqlCommand($"DROP DATABASE \"{restored.DatabaseName}\" WITH (FORCE)", connection) { CommandTimeout = 120 }; await cleanupDatabase.ExecuteNonQueryAsync();
            }
        }
    }
}

internal sealed class BackupTestDirectory : IDisposable
{
    public static string Workspace
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !Directory.Exists(System.IO.Path.Combine(current.FullName, ".local", "pgsql-17.10-client"))) current = current.Parent;
            return current?.FullName ?? throw new InvalidOperationException("Native PostgreSQL clients are required for backup verification.");
        }
    }
    public string Path { get; } = System.IO.Path.Combine(Workspace, ".superpowers", "sdd", "2026-10-08-roadmap-completion", "backup-tests", Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Workspace, ".superpowers", "sdd", "2026-10-08-roadmap-completion", "backup-tests")) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(Path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
