using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Api.DeveloperIntegration;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class DeveloperDocumentationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task AnonymousReadsPublicCatalog()
    {
        using var client = fixture.CreateClient();
        var response = await client.GetAsync("/api/integrations/developer-docs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var index = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(9, index.GetArrayLength());
        Assert.Equal(new[] { "activation", "device-release", "errors", "heartbeat", "logout", "offline", "quick-start", "trial", "versions" },
            index.EnumerateArray().Select(x => x.GetProperty("topic").GetString()).Order().ToArray());
        foreach (var entry in index.EnumerateArray())
        {
            Assert.Equal("2026-10-11", entry.GetProperty("version").GetString());
            var document = await client.GetFromJsonAsync<JsonElement>("/api/integrations/developer-docs/" + entry.GetProperty("id").GetString());
            Assert.False(string.IsNullOrWhiteSpace(document.GetProperty("markdown").GetString()));
            Assert.Equal(entry.GetProperty("version").GetString(), document.GetProperty("version").GetString());
        }
    }

    [Fact]
    public async Task UnknownDocumentReturns404()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/integrations/developer-docs/not-a-document")).StatusCode);
    }

    [Fact]
    public void CatalogRoutesExist()
    {
        var endpoints = fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        foreach (var path in new[] { "/api/integrations/developer-docs", "/api/integrations/developer-docs/{id}" })
            Assert.Contains(endpoints, x => x.RoutePattern.RawText == path && x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        var catalog = new DeveloperDocumentationCatalog();
        foreach (var operation in new[] { "activate", "trial-activate", "heartbeat", "logout", "device-release", "version", "offline-verify" })
        {
            var reference = catalog.GetApiReference(operation);
            Assert.NotNull(reference);
            Assert.Contains(endpoints, x => x.RoutePattern.RawText == reference.Path && x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(reference.Method));
            Assert.False(string.IsNullOrWhiteSpace(reference.Authentication));
            Assert.False(string.IsNullOrWhiteSpace(reference.Markdown));
        }
    }

    [Fact]
    public void CatalogHasNoBusinessDependencies()
    {
        var catalog = new DeveloperDocumentationCatalog();
        Assert.Equal(9, catalog.List().Count);
        foreach (var summary in catalog.List())
        {
            var document = catalog.Get(summary.Id);
            Assert.NotNull(document);
            Assert.False(string.IsNullOrWhiteSpace(document.Markdown));
            Assert.Equal(summary.Version, document.Version);
        }
        Assert.Null(catalog.Get("not-a-document"));
        Assert.Null(catalog.GetApiReference("admin"));
        Assert.Empty(catalog.Search("unmatched-unique-query", 5));
        Assert.Contains(catalog.Search("ACTIVATION", 10), x => x.Id == "activation");
        Assert.Single(catalog.Search("sessionToken", 1));
        Assert.Throws<ArgumentException>(() => catalog.Search(" ", 5));
        Assert.Throws<ArgumentException>(() => catalog.Search(new string('x', 201), 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Search("license", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Search("license", 11));
    }
}
