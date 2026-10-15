using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using KeyValidation.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KeyValidation.Infrastructure;
public sealed class WebhookTargetPolicy(IConfiguration configuration, IHostEnvironment environment)
{
    private readonly HashSet<string> hosts = (configuration.GetSection("Webhooks:AllowedHosts").Get<string[]>() ?? []).Select(x => x.Trim().TrimEnd('.').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
    private readonly bool local = (environment.IsDevelopment() || environment.IsEnvironment("Testing")) && configuration.GetValue("Webhooks:AllowLoopbackHttp", false);
    public Uri Validate(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !hosts.Contains(uri.IdnHost.TrimEnd('.').ToLowerInvariant()) || uri.Scheme != Uri.UriSchemeHttps && !(local && uri.Scheme == Uri.UriSchemeHttp))
            throw new RuleException("WEBHOOK_TARGET_BLOCKED", 400);
        if (IPAddress.TryParse(uri.IdnHost, out var address))
            ValidateAddress(uri, address);
        else if (uri.Scheme == Uri.UriSchemeHttp && !string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new RuleException("WEBHOOK_TARGET_BLOCKED", 400);
        return uri;
    }

    public void ValidateAddress(Uri uri, IPAddress address)
    {
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!local || !IPAddress.IsLoopback(address))
                throw new RuleException("WEBHOOK_TARGET_BLOCKED", 400);
        }
        else if (!IsPublic(address))
            throw new RuleException("WEBHOOK_TARGET_BLOCKED", 400);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] is not 0 and not 10 and not 127 && bytes[0] < 224 && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0)) && !(bytes[0] == 198 && bytes[1] is 18 or 19);
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x02) && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }
}
