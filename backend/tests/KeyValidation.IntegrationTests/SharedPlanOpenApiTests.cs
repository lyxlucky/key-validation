using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanOpenApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ContractSeparatesGlobalPlansFromApplicationOptions()
    {
        using var client = fixture.CreateClient();
        var api = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = api.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/admin/plans").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/admin/applications/{id}/plan-references/{planId}")
            .TryGetProperty("put", out _));
        var schemas = api.GetProperty("components").GetProperty("schemas");
        var global = schemas.GetProperty("GlobalPlanDto").GetProperty("properties");
        Assert.False(global.TryGetProperty("applicationId", out _));
        Assert.True(global.TryGetProperty("referenceCount", out _));
        Assert.True(schemas.GetProperty("ReportPlanDto").GetProperty("properties")
            .TryGetProperty("isEnabled", out _));
    }
}
