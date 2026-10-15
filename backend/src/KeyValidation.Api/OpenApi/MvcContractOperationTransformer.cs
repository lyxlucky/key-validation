using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KeyValidation.Api;

/// <summary>Keeps JSON request documentation consistent with the existing public contract.</summary>
public sealed class MvcContractOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // The JSON formatter still accepts vendor +json types; do not advertise a wildcard schema.
        operation.RequestBody?.Content?.Remove("application/*+json");
        operation.RequestBody?.Content?.Remove("text/json");
        if (operation.Responses is not null)
            foreach (var response in operation.Responses.Values)
                if (response.Content is { Count: > 0 } content && content.Values.All(media => media.Schema is null))
                    content.Clear();
        return Task.CompletedTask;
    }
}
