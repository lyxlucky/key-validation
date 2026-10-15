using KeyValidation.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KeyValidation.Tests;

public class SecretTests
{
    private static SecretService Service() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Licensing:CurrentKeyVersion"] = "v1",
        ["Licensing:Keys:v1"] = Convert.ToBase64String(Enumerable.Range(0,32).Select(x => (byte)x).ToArray())
    }).Build());
    [Fact] public void KeyNormalizationAcceptsCaseAndDisplaySeparators()
    {
        var service = Service(); var key = service.GenerateLicense();
        var variant = "  " + key.Plaintext.ToLowerInvariant().Replace("-", " ") + "  ";
        Assert.Equal(key.Digest, service.DigestLicense(variant).Digest);
        Assert.DoesNotContain(key.Plaintext, key.Hint);
        Assert.Equal(64, key.Digest.Length);
    }
    [Fact] public void InvalidCharactersAreNotSilentlyDiscarded()
    {
        var service = Service(); var key = service.GenerateLicense();
        Assert.Throws<KeyValidation.Domain.RuleException>(() => service.DigestLicense(key.Plaintext + "!"));
    }
    [Fact] public void TokensHave256RandomBitsAndOnlyTheirDigestIsReproducible()
    {
        var service = Service(); var one = service.GenerateSession(); var two = service.GenerateSession();
        Assert.Equal(43, one.Token.Length); Assert.NotEqual(one.Token, two.Token);
        Assert.Equal(one.Digest, service.DigestToken(one.Token));
        Assert.NotEqual(one.Token, one.Digest);
    }
    [Fact] public void DeviceDigestsAreApplicationScoped()
    {
        var service = Service();
        Assert.NotEqual(service.DigestDevice(Guid.Parse("00000000-0000-0000-0000-000000000001"), "device"), service.DigestDevice(Guid.Parse("00000000-0000-0000-0000-000000000002"), "device"));
    }
}
