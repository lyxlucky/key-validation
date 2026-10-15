using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyValidation.Api.DeveloperIntegration;

[McpServerToolType]
public sealed class DeveloperMcpTools(DeveloperDocumentationCatalog docs, DeveloperExampleCatalog examples, DeveloperTroubleshootingCatalog troubleshooting)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static CallToolResult Run(Func<object> action)
    {
        try { return new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(action(), Json) }] }; }
        catch (ArgumentException error) { return new() { IsError = true, Content = [new TextContentBlock { Text = error.Message }] }; }
    }

    [McpServerTool(Name = "search_integration_docs", ReadOnly = true, OpenWorld = false), Description("Search public integration documentation. Query 1–200 characters; limit 1–10.")]
    public CallToolResult Search(string query, int limit = 5) => Run(() => docs.Search(query, limit).Select(x => new { x.Id, x.Title, Summary = x.Topic, ResourceUri = $"keygate://docs/{x.Id}" }).ToArray());

    [McpServerTool(Name = "get_integration_doc", ReadOnly = true, OpenWorld = false), Description("Read a public document by its catalog ID.")]
    public CallToolResult Document(string docId) => Run(() => docs.Get(docId) ?? throw new ArgumentException("Unknown document ID."));

    [McpServerTool(Name = "get_api_reference", ReadOnly = true, OpenWorld = false), Description("Read a public client API reference by operation ID.")]
    public CallToolResult Api(string operationId) => Run(() => docs.GetApiReference(operationId) ?? throw new ArgumentException("Unknown operation ID."));

    [McpServerTool(Name = "get_code_example", ReadOnly = true, OpenWorld = false), Description("Read a fixed csharp/python/javascript example for online-license.")]
    public CallToolResult Example(string language, string scenario) => Run(() => examples.Get(language, scenario));

    [McpServerTool(Name = "get_troubleshooting_guide", ReadOnly = true, OpenWorld = false), Description("Read guidance for an error code (1–100 characters), optionally scoped to a public operation.")]
    public CallToolResult Troubleshooting(string errorCode, string? operationId = null) => Run(() => troubleshooting.Get(errorCode, operationId));
}
