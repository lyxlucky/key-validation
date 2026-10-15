namespace KeyValidation.Api.DeveloperIntegration;

public sealed record DeveloperCodeExample(string Language, string Scenario, string Code, string Instructions, string Version);

public sealed class DeveloperExampleCatalog
{
    public DeveloperCodeExample Get(string language, string scenario)
    {
        if (scenario != "online-license") throw new ArgumentException("Unsupported example scenario.", nameof(scenario));
        var (extension, command) = language switch
        {
            "python" => ("py", "Python 3.11+，仅标准库：python online-license.py [--once]"),
            "javascript" => ("mjs", "Node.js 22+，原生 fetch，无需安装包：node online-license.mjs [--once]"),
            "csharp" => ("cs", ".NET 10 SDK：dotnet new console -n OnlineLicense -f net10.0；用 online-license.cs 替换 OnlineLicense/Program.cs；dotnet run --project OnlineLicense -- [--once]"),
            _ => throw new ArgumentException("Unsupported example language.", nameof(language))
        };
        var assembly = typeof(DeveloperExampleCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream($"KeyValidation.Api.DeveloperIntegration.Examples.online-license.{extension}")
            ?? throw new InvalidOperationException("Example resource missing.");
        using var reader = new StreamReader(stream);
        var instructions = command + "\n设置 KEYVALIDATION_API_URL（API origin，不含 /api）、KEYVALIDATION_APP_CODE、KEYVALIDATION_LICENSE_KEY、KEYVALIDATION_REQUIRED_FEATURE。"
            + "KEYVALIDATION_DEVICE_ID 可选；缺省在用户目录 .keyvalidation/device-id 保存随机稳定标识。会话令牌仅保存在内存中。KEYVALIDATION_CLIENT_VERSION 可选（三段数字，例如 1.2.3），应用强制版本时必须设置。"
            + "生产环境使用 HTTPS。--once 演示一次业务操作及心跳后退出；默认持续运行，Ctrl+C 退出并尽力 logout。"
            + "将受保护业务放在门禁之后，每次操作和挂起恢复重新检查。临时网络故障仅在最早期限内重试。"
            + "离线 ES256 验证见 offline 文档及 samples/KeyValidation.Client/README.md。";
        if (language == "python")
            instructions += "Python urllib 的超时约束每次 socket 操作；持续缓慢传输可能延迟退出和 logout。阻塞期间不执行受保护操作，超过旧租约期限的响应不会恢复授权。";
        return new(language, scenario, reader.ReadToEnd(), instructions, DeveloperDocumentationCatalog.Version);
    }
}
