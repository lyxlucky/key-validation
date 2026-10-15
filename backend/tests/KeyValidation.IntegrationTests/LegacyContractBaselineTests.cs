using System.Text.Json;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class LegacyContractBaselineTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CaptureLegacyOpenApiForAbpCompatibility()
    {
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using (document)
        {
            Assert.NotEmpty(document.RootElement.GetProperty("paths").EnumerateObject());
            Assert.True(document.RootElement.TryGetProperty("components", out _));
            // Only an explicitly requested capture writes a baseline. Normal runs
            // never overwrite the reviewed reference document.
            if (Environment.GetEnvironmentVariable("KEYVALIDATION_OPENAPI_BASELINE") is { Length: > 0 } path)
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document.RootElement,
                    new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }
}
