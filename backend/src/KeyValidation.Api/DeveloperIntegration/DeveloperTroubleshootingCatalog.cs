namespace KeyValidation.Api.DeveloperIntegration;
public sealed record DeveloperTroubleshootingGuide(string ErrorCode, bool Known, string Markdown, string Version);
public sealed class DeveloperTroubleshootingCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Guides = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["APPLICATION_DISABLED"] = "应用已停用。停止受保护功能，联系应用提供方。",
        ["LICENSE_INVALID"] = "授权无效。停止受保护功能，核对应用标识与卡密来源。不要输出卡密。",
        ["LICENSE_DISABLED"] = "授权已停用。停止受保护功能，联系提供方。",
        ["LICENSE_EXPIRED"] = "授权已到期。停止受保护功能，按提供方流程续期后重新激活。",
        ["SESSION_INVALID"] = "会话无效。停止受保护功能；确认使用激活返回的会话令牌。",
        ["SESSION_REVOKED"] = "会话已撤销。立即停止受保护功能；排查同设备重复激活。",
        ["SESSION_EXPIRED"] = "会话或租约已到期。停止受保护功能；恢复网络后重新激活。",
        ["BINDING_RELEASED"] = "设备绑定已释放。停止受保护功能；确认设备解绑记录。",
        ["DEVICE_LIMIT_REACHED"] = "设备数已满。停止受保护功能，按提供方流程释放旧绑定。",
        ["SESSION_LIMIT_REACHED"] = "在线会话数已满。退出旧会话，等待其租约失效或联系提供方。",
        ["ACTIVATION_DEADLINE_PASSED"] = "已超过首次激活截止期。停止受保护功能并联系提供方更换授权。",
        ["CLIENT_VERSION_REQUIRED"] = "请求缺少客户端版本。提供符合契约的 clientVersion，停止受保护功能直到成功授权。",
        ["CLIENT_VERSION_INVALID"] = "版本格式无效。使用三段数字版本，停止受保护功能直到成功授权。",
        ["CLIENT_UPGRADE_REQUIRED"] = "版本低于最低要求。停止受保护功能；按可信更新流程升级。",
        ["TRIAL_DISABLED"] = "试用已停用。停止受保护功能，联系提供方。",
        ["TRIAL_EXPIRED"] = "试用已到期。停止受保护功能；正式激活不会重置试用期限。",
        ["TRIAL_UPGRADED"] = "试用已升级。停止旧试用会话，使用正式卡密激活。",
        ["429"] = "请求过于频繁。读取 Retry-After（秒或 HTTP 日期），有界退避等待不得超过当前最早截止期限；到期停止受保护功能。"
    };
    public DeveloperTroubleshootingGuide Get(string errorCode, string? operationId)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 100)
            throw new ArgumentException("Error code must contain 1–100 characters.", nameof(errorCode));
        if (operationId is not null && new DeveloperDocumentationCatalog().GetApiReference(operationId)is null)
            throw new ArgumentException("Unknown operation ID.", nameof(operationId));
        var known = Guides.TryGetValue(errorCode, out var markdown);
        markdown ??= "此错误码未收录；不推断服务端原因。核对 HTTP 状态、请求字段及公开接口文档，保留脱敏 traceId 联系提供方。明确拒绝或租约到期立即停止受保护功能；网络故障与 5xx 仅在有效租约内有界重试。不要提交卡密、令牌或原始日志。";
        return new(errorCode, known, markdown, DeveloperDocumentationCatalog.Version);
    }
}
