using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public static class ClientVersionRules
{
    public static ClientVersionInfo Evaluate(ApplicationVersionPolicy? policy, string? clientVersion)
    {
        if (clientVersion is not null && !ClientVersion.TryParse(clientVersion, out _))
            throw new RuleException("CLIENT_VERSION_INVALID", 400);
        var hasVersion = ClientVersion.TryParse(clientVersion, out var version);
        var upgrade = hasVersion && ClientVersion.TryParse(policy?.LatestVersion, out var latest) && version.CompareTo(latest) < 0;
        var info = new ClientVersionInfo(clientVersion, policy?.MinimumVersion, policy?.LatestVersion, policy?.EnforceMinimum ?? false, upgrade, policy?.UpdateUrl, policy?.ReleaseNotes, policy?.Sha256);
        if (policy?.EnforceMinimum == true)
        {
            if (!hasVersion)
                throw new ClientVersionException("CLIENT_VERSION_REQUIRED", info);
            if (ClientVersion.TryParse(policy.MinimumVersion, out var minimum) && version.CompareTo(minimum) < 0)
                throw new ClientVersionException("CLIENT_UPGRADE_REQUIRED", info);
        }

        return info;
    }

    public static void Validate(UpdateVersionPolicy policy, bool allowLoopbackHttp)
    {
        static void Require(bool condition)
        {
            if (!condition)
                throw new RuleException("VALIDATION_ERROR", 400);
        }

        Require(policy.MinimumVersion is null || ClientVersion.TryParse(policy.MinimumVersion, out _));
        Require(policy.LatestVersion is null || ClientVersion.TryParse(policy.LatestVersion, out _));
        Require(!policy.EnforceMinimum || policy.MinimumVersion is not null);
        if (ClientVersion.TryParse(policy.MinimumVersion, out var minimum))
            Require(ClientVersion.TryParse(policy.LatestVersion, out var latest) && latest.CompareTo(minimum) >= 0);
        Require(policy.ReleaseNotes?.Length is not > 4000);
        Require(policy.Sha256 is null || policy.Sha256.Length == 64 && policy.Sha256.All(Uri.IsHexDigit));
        if (policy.UpdateUrl is not null)
        {
            Require(policy.UpdateUrl.Length is> 0 and <= 2048 && Uri.TryCreate(policy.UpdateUrl, UriKind.Absolute, out _));
            var uri = new Uri(policy.UpdateUrl, UriKind.Absolute);
            Require(string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host) && (uri.Scheme == Uri.UriSchemeHttps || allowLoopbackHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
        }
    }
}
