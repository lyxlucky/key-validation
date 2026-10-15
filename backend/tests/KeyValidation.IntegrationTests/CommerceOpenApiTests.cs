using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommerceOpenApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OpenApiGeneratesCommerceSchemasAndIndependentBearerRequirements()
    {
        using var client = fixture.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = await response.Content.ReadFromJsonAsync<JsonElement>();
            var components = document.GetProperty("components");
            Assert.True(components.GetProperty("schemas").TryGetProperty("OrderDto", out _));
            Assert.True(components.GetProperty("schemas").TryGetProperty("ApiCredentialSecretDto", out _));
            var paths = document.GetProperty("paths");
            Assert.True(paths.GetProperty("/api/v1/orders").GetProperty("post").GetProperty("security")[0].TryGetProperty("ApiCredentialBearer", out _));
            Assert.True(paths.GetProperty("/api/portal/orders").GetProperty("get").GetProperty("security")[0].TryGetProperty("PortalBearer", out _));
            Assert.False(paths.GetProperty("/api/portal/sessions").GetProperty("post").TryGetProperty("security", out _));
        }
    }
}
