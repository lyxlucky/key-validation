using KeyValidation.Api.DeveloperIntegration;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class DeveloperExampleTests
{
    [Theory]
    [InlineData("csharp")]
    [InlineData("python")]
    [InlineData("javascript")]
    public void AllSupportedExamplesLoad(string language)
    {
        var example = new DeveloperExampleCatalog().Get(language, "online-license");
        Assert.False(string.IsNullOrWhiteSpace(example.Code));
        Assert.False(string.IsNullOrWhiteSpace(example.Instructions));
        Assert.Equal(language, example.Language);
        Assert.Equal(DeveloperDocumentationCatalog.Version, example.Version);
    }

    [Theory]
    [InlineData("ruby", "online-license")]
    [InlineData("python", "offline")]
    public void UnsupportedExampleRejected(string language, string scenario) =>
        Assert.Throws<ArgumentException>(() => new DeveloperExampleCatalog().Get(language, scenario));

    [Fact]
    public void UnknownErrorIsExplicitlyUnknown()
    {
        var guide = new DeveloperTroubleshootingCatalog().Get("UNLISTED_ERROR", null);
        Assert.False(guide.Known);
        Assert.Contains("未收录", guide.Markdown);
        Assert.Equal(DeveloperDocumentationCatalog.Version, guide.Version);
    }

    [Fact]
    public void ExplicitDenialStopsProtectedUse()
    {
        var guide = new DeveloperTroubleshootingCatalog().Get("SESSION_REVOKED", "heartbeat");
        Assert.True(guide.Known);
        Assert.Contains("停止", guide.Markdown);
    }
}
