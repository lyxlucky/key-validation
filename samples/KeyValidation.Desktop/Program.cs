using KeyValidation.Client;

namespace KeyValidation.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        DesktopOptions? options = null;
        try
        {
            options = DesktopOptions.Parse(args);
            if (options.SmokeManifest is not null) return GuiSmokeRunner.Run(options);
            var directory = options.StateDirectory ?? LocalPreferences.DefaultDirectory;
            var diagnostics = new SafeDiagnostics();
            var settings = ClientSettings.Load(Path.Combine(AppContext.BaseDirectory, "client-settings.json"));
            var profile = options.ProfilePath is { } profilePath
                ? DemoProfile.LoadAsync(profilePath).GetAwaiter().GetResult()
                : settings.CreateProfile(LocalPreferences.LoadDeviceId(directory));
            var coordinator = new SessionCoordinator(p => DesktopTransport.Create(p, diagnostics), new FileTrustedTimeStore(Path.Combine(directory, "trusted-time.json")));
            using var form = new ClientForm(coordinator, profile, settings.ApplicationName);
            Application.ThreadException += (_, _) => form.ReportError();
            Application.Run(form);
            return 0;
        }
        catch
        {
            if (args.Contains("--smoke-manifest", StringComparer.Ordinal))
            {
                if (options?.SmokeOutput is { } output) GuiSmokeRunner.WriteStartupFailure(output);
            }
            else MessageBox.Show("程序未能启动。请检查参数、配置文件和本地状态目录的访问权限；不会自动重置可信时间记录。", "KeyGate 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }
}

internal sealed record DesktopOptions(string? ProfilePath, string? SmokeManifest, string? SmokeOutput, string? StateDirectory)
{
    public static DesktopOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--profile" or "--smoke-manifest" or "--smoke-output" or "--state-directory") ||
                !values.TryAdd(args[i], Path.GetFullPath(args[i + 1]))) throw new ProfileException("PROFILE_INVALID");
        }
        values.TryGetValue("--profile", out var profile); values.TryGetValue("--smoke-manifest", out var manifest);
        values.TryGetValue("--smoke-output", out var output); values.TryGetValue("--state-directory", out var state);
        if (manifest is not null && (output is null || profile is not null) || manifest is null && output is not null) throw new ProfileException("PROFILE_INVALID");
        return new(profile, manifest, output, state);
    }
}
