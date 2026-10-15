using System.Net;
using KeyValidation.Application;

namespace KeyValidation.Api;

public sealed class TrustedCountryMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly IPAddress[] proxies = (configuration.GetSection("Risk:TrustedCountryProxies").Get<string[]>() ?? []).Select(IPAddress.Parse).ToArray();
    public async Task InvokeAsync(HttpContext context, ClientRequestFacts facts)
    {
        // This runs before ForwardedHeaders changes RemoteIpAddress. The claimed
        // client IP and the country header can never establish their own trust.
        var direct = context.Connection.RemoteIpAddress;
        var value = context.Request.Headers["X-KeyGate-Country"];
        if (direct is not null && proxies.Any(proxy => proxy.MapToIPv6().Equals(direct.MapToIPv6())) && value.Count == 1)
        {
            var country = value[0]?.ToUpperInvariant();
            if (country is { Length: 2 } && country.All(character => character is >= 'A' and <= 'Z')) facts.TrustedCountryCode = country;
        }
        await next(context);
    }
}
