using System.Net;
using KeyValidation.Infrastructure;
using Xunit;

namespace KeyValidation.Tests;

public sealed class WebhookAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2001:db8::1")]
    public void NonPublicAndTranslationDestinationsAreRejected(string address) => Assert.False(WebhookTargetPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicDestinationsRemainEligibleForExplicitAllowlisting(string address) => Assert.True(WebhookTargetPolicy.IsPublic(IPAddress.Parse(address)));
}
