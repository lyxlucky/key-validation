using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class HttpErrorContractTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UnknownDeveloperDocumentRetainsThePublicProblemContract()
    {
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync("/api/integrations/developer-docs/not-a-document");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.Equal("NOT_FOUND", problem.GetProperty("title").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnsupportedActivationContentTypeRetainsTheValidationProblemContract()
    {
        using var client = fixture.CreateClient();
        using var body = new StringContent("{}", Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync("/api/client/licenses/activate", body);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Equal(415, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
