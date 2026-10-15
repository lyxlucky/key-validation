namespace KeyValidation.Api.DeveloperIntegration;
/// <summary>Explicit public content only; loading never resolves business services or reads configuration.</summary>
public sealed class DeveloperDocumentationCatalog
{
    public const string Version = "2026-10-11";
    private readonly Dictionary<string, DeveloperDocument> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeveloperApiReference> references = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<DeveloperDocumentSummary> summaries;
    public DeveloperDocumentationCatalog()
    {
        Add("quick-start", "快速开始", "quick-start");
        Add("activation", "卡密激活", "activation", "activate", "POST", "/api/client/licenses/activate", "匿名；请求体携带 licenseKey");
        Add("trial", "试用授权", "trial", "trial-activate", "POST", "/api/client/trials/activate", "匿名；无需卡密");
        Add("heartbeat", "心跳与功能门禁", "heartbeat", "heartbeat", "POST", "/api/client/sessions/heartbeat", "Authorization: Bearer <sessionToken>");
        Add("logout", "退出会话", "logout", "logout", "POST", "/api/client/sessions/logout", "Authorization: Bearer <sessionToken>");
        Add("device-release", "设备解绑", "device-release", "device-release", "POST", "/api/client/device-bindings/release", "Authorization: Bearer <sessionToken>");
        Add("versions", "版本检查", "versions", "version", "GET", "/api/client/applications/{appCode}/version", "匿名");
        Add("offline", "离线验证", "offline", "offline-verify", "POST", "/api/client/offline-licenses/verify", "匿名；请求体携带签名授权文件");
        Add("errors", "常见错误", "errors");
        summaries = Array.AsReadOnly(documents.Values.Select(Summary).ToArray());
    }

    public IReadOnlyList<DeveloperDocumentSummary> List() => summaries;
    public DeveloperDocument? Get(string id) => documents.GetValueOrDefault(id);
    public DeveloperApiReference? GetApiReference(string operationId) => references.GetValueOrDefault(operationId);
    public IReadOnlyList<DeveloperDocumentSummary> Search(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
            throw new ArgumentException("Query must contain 1–200 characters.", nameof(query));
        if (limit is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be 1–10.");
        var term = query.Trim();
        return Array.AsReadOnly(documents.Values.Where(d => d.Id.Contains(term, StringComparison.OrdinalIgnoreCase) || d.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || d.Topic.Contains(term, StringComparison.OrdinalIgnoreCase) || d.Markdown.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(limit).Select(Summary).ToArray());
    }

    private static DeveloperDocumentSummary Summary(DeveloperDocument document) => new(document.Id, document.Title, document.Topic, document.Version);
    private void Add(string id, string title, string topic, string? operationId = null, string? method = null, string? path = null, string? authentication = null)
    {
        var assembly = typeof(DeveloperDocumentationCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream($"KeyValidation.Api.DeveloperIntegration.Content.{id}.md") ?? throw new InvalidOperationException($"Missing developer document: {id}");
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(markdown))
            throw new InvalidOperationException($"Empty developer document: {id}");
        if (!documents.TryAdd(id, new(id, title, topic, markdown, Version)))
            throw new InvalidOperationException($"Duplicate developer document ID: {id}");
        if (operationId is not null && !references.TryAdd(operationId, new(operationId, method!, path!, authentication!, markdown, Version)))
            throw new InvalidOperationException($"Duplicate developer operation ID: {operationId}");
    }
}
