using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KeyValidation.Api;
public sealed class SecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Key Validation API";
        document.Info.Description = "Admin uses Identity cookies and X-CSRF-TOKEN on all POST/PATCH/PUT/DELETE requests, including login and search. Fetch /api/admin/auth/csrf before login and again afterward. Client, portal and API credential bearer tokens are independent and grant no admin access. Timestamps are UTC. Credentials are returned only at creation/rotation; order delivery keys remain recoverable through the scoped delivery endpoint for 30 days. All secret responses use no-store. Authorization uses assigned role permissions intersected with application/channel scope. /api/v1 writes require requestId; changed-content replays return 409. API credentials permit 60 requests/minute each, plus the global limit. All CNY amounts are integer cents. See docs/api-contract.md for issuance response-loss recovery.";
        document.Components ??= new();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["AdminCookie"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "KeyValidation.Admin"
            },
            ["CsrfCookie"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "KeyValidation.Csrf"
            },
            ["ClientBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Opaque client session token; not an admin credential."
            },
            ["PortalBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Independent eight-hour customer portal session from POST /api/portal/sessions."
            },
            ["ApiCredentialBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Application/channel API credential; permissions are licenses.read, customers.manage and/or orders.manage."
            }
        };
        var problem = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>
            {
                "type",
                "title",
                "status",
                "code",
                "traceId"
            },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                },
                ["title"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                },
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer
                },
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                },
                ["traceId"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String
                },
                ["batchId"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "uuid",
                    Description = "Present on an issuance idempotency conflict."
                }
            }
        };
        foreach (var(path, item)in document.Paths)
            foreach (var(method, operation)in item.Operations ?? [])
            {
                operation.Responses ??= new();
                foreach (var status in new[]
                {
                    "400",
                    "401",
                    "403",
                    "404",
                    "409",
                    "410",
                    "413",
                    "429",
                    "500"
                }

                )
                    operation.Responses.TryAdd(status, new OpenApiResponse { Description = "Problem Details with stable code and traceId.", Content = new Dictionary<string, OpenApiMediaType> { ["application/problem+json"] = new() { Schema = problem } } });
                var requirement = new OpenApiSecurityRequirement();
                if (path.StartsWith("/api/admin", StringComparison.Ordinal) && path is not "/api/admin/auth/login" and not "/api/admin/auth/two-factor" and not "/api/admin/auth/csrf")
                    requirement.Add(new("AdminCookie", document), []);
                if (path.StartsWith("/api/admin", StringComparison.Ordinal) && method != HttpMethod.Get)
                {
                    requirement.Add(new("CsrfCookie", document), []);
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter { Name = "X-CSRF-TOKEN", In = ParameterLocation.Header, Required = true, Description = "Request token returned by /api/admin/auth/csrf.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
                }

                if (path.StartsWith("/api/client/sessions", StringComparison.Ordinal) || path == "/api/client/device-bindings/release")
                    requirement.Add(new("ClientBearer", document), []);
                if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
                    requirement.Add(new("ApiCredentialBearer", document), []);
                if (path.StartsWith("/api/portal/", StringComparison.Ordinal) && !(path == "/api/portal/sessions" && method == HttpMethod.Post))
                    requirement.Add(new("PortalBearer", document), []);
                if (requirement.Count > 0)
                    operation.Security = [requirement];
            }

        return Task.CompletedTask;
    }
}
