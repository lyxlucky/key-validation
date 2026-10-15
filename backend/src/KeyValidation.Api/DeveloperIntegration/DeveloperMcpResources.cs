using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyValidation.Api.DeveloperIntegration;
public sealed class DeveloperMcpResources(DeveloperDocumentationCatalog catalog)
{
    public ListResourcesResult List() => new()
    {
        Resources = catalog.List().Select(x => new Resource { Uri = $"keygate://docs/{x.Id}", Name = x.Id, Title = x.Title, MimeType = "text/markdown" }).ToList()
    };
    public ReadResourceResult Read(string uri)
    {
        const string prefix = "keygate://docs/";
        var document = uri.StartsWith(prefix, StringComparison.Ordinal) ? catalog.Get(uri[prefix.Length..]) : null;
        if (document is null)
            throw new McpProtocolException("Unknown public document resource.", McpErrorCode.InvalidParams);
        return new()
        {
            Contents = [new TextResourceContents
            {
                Uri = uri,
                MimeType = "text/markdown",
                Text = document.Markdown
            }

            ]
        };
    }
}
