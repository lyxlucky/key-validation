using KeyValidation.Api;
using KeyValidation.Application;

var builder = WebApplication.CreateBuilder(args);
builder.AddKeyValidation();
var app = builder.Build();
if (args.Length > 0 && args[0] == "backup")
{
    Environment.ExitCode = await BackupCli.Run(app.Services, args);
    return;
}

_ = app.Services.GetRequiredService<ISecretService>();
if (args.Length > 0 && args[0] is "migrate" or "admin")
{
    Environment.ExitCode = await AdminCli.Run(app.Services, args);
    return;
}

app.UseKeyValidation();
app.MapKeyValidation();
app.Run();
public partial class Program;
