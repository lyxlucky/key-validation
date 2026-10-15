using System.Text.Json;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class BackupCli
{
    public static async Task<int> Run(IServiceProvider services, string[] arguments, TextWriter? output = null, TextWriter? errorOutput = null)
    {
        output ??= Console.Out; errorOutput ??= Console.Error;
        if (arguments.Length < 2 || arguments[0] != "backup" || arguments[1] is not "create" and not "verify" and not "restore" ||
            arguments[1] == "create" && arguments.Length != 2 || arguments[1] != "create" && (arguments.Length != 3 || !Guid.TryParse(arguments[2], out _)))
        {
            await errorOutput.WriteLineAsync("Usage: backup create | backup verify <backup-id> | backup restore <backup-id>"); return 2;
        }
        await using var scope = services.CreateAsyncScope();
        try
        {
            object result;
            if (arguments[1] == "create") { var job = await scope.ServiceProvider.GetRequiredService<BackupService>().CreateAsync(); result = job; await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))); return job.Status == "succeeded" ? 0 : 1; }
            if (arguments[1] == "verify") { var verification = await scope.ServiceProvider.GetRequiredService<BackupService>().VerifyAsync(Guid.Parse(arguments[2])); result = verification; await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))); return verification.Status == "succeeded" ? 0 : 1; }
            result = await scope.ServiceProvider.GetRequiredService<BackupRecoveryService>().RestoreAsync(Guid.Parse(arguments[2]));
            await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))); return 0;
        }
        catch (BackupFailure failure) { await errorOutput.WriteLineAsync(failure.Code); return 1; }
        catch (KeyValidation.Domain.RuleException failure) { await errorOutput.WriteLineAsync(failure.Code); return 1; }
        catch (Exception error) { await errorOutput.WriteLineAsync("BACKUP_OPERATION_FAILED: " + error.GetType().Name); return 1; }
    }
}
