using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public readonly record struct ClientVersion(int Major, int Minor, int Patch) : IComparable<ClientVersion>
{
    public static bool TryParse(string? value, out ClientVersion version)
    {
        version = default;
        if (value is null || value.Length is < 5 or > 14 || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z", RegexOptions.CultureInvariant)) return false;
        var parts = value.Split('.');
        version = new(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture)); return true;
    }
    public int CompareTo(ClientVersion other)
    {
        var major = Major.CompareTo(other.Major); if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor); return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }
}

public static class ClientVersionRules
{
    public static ClientVersionInfo Evaluate(ApplicationVersionPolicy? policy, string? clientVersion)
    {
        if (clientVersion is not null && !ClientVersion.TryParse(clientVersion, out _)) throw new RuleException("CLIENT_VERSION_INVALID", 400);
        var hasVersion = ClientVersion.TryParse(clientVersion, out var version);
        var upgrade = hasVersion && ClientVersion.TryParse(policy?.LatestVersion, out var latest) && version.CompareTo(latest) < 0;
        var info = new ClientVersionInfo(clientVersion, policy?.MinimumVersion, policy?.LatestVersion, policy?.EnforceMinimum ?? false,
            upgrade, policy?.UpdateUrl, policy?.ReleaseNotes, policy?.Sha256);
        if (policy?.EnforceMinimum == true)
        {
            if (!hasVersion) throw new ClientVersionException("CLIENT_VERSION_REQUIRED", info);
            if (ClientVersion.TryParse(policy.MinimumVersion, out var minimum) && version.CompareTo(minimum) < 0)
                throw new ClientVersionException("CLIENT_UPGRADE_REQUIRED", info);
        }
        return info;
    }

    public static void Validate(UpdateVersionPolicy policy, bool allowLoopbackHttp)
    {
        static void Require(bool condition) { if (!condition) throw new RuleException("VALIDATION_ERROR", 400); }
        Require(policy.MinimumVersion is null || ClientVersion.TryParse(policy.MinimumVersion, out _));
        Require(policy.LatestVersion is null || ClientVersion.TryParse(policy.LatestVersion, out _));
        Require(!policy.EnforceMinimum || policy.MinimumVersion is not null);
        if (ClientVersion.TryParse(policy.MinimumVersion, out var minimum))
            Require(ClientVersion.TryParse(policy.LatestVersion, out var latest) && latest.CompareTo(minimum) >= 0);
        Require(policy.ReleaseNotes?.Length is not > 4000);
        Require(policy.Sha256 is null || policy.Sha256.Length == 64 && policy.Sha256.All(Uri.IsHexDigit));
        if (policy.UpdateUrl is not null)
        {
            Require(policy.UpdateUrl.Length is > 0 and <= 2048 && Uri.TryCreate(policy.UpdateUrl, UriKind.Absolute, out _));
            var uri = new Uri(policy.UpdateUrl, UriKind.Absolute);
            Require(string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host) &&
                (uri.Scheme == Uri.UriSchemeHttps || allowLoopbackHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
        }
    }
}

public sealed class ClientVersionPolicyService(ILicensingData db, ApplicationAccessScope access, TimeProvider clock, ClientVersionOptions options)
{
    public async Task<VersionPolicyDto> Get(Guid applicationId)
    {
        if (!access.Allows(ApplicationPermissions.Administrator) && !access.Allows(ApplicationPermissions.ReadLicenses)) throw new RuleException("FORBIDDEN", 403);
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId)) throw new RuleException("NOT_FOUND", 404);
        return Dto(await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId) ?? new() { ApplicationId = applicationId });
    }
    public async Task<PublicVersionDto> Public(string applicationCode)
    {
        var code = ApplicationCode.Normalize(applicationCode);
        var app = await db.Applications.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code) ?? throw new RuleException("NOT_FOUND", 404);
        if (app.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        var policy = await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == app.Id);
        return new(app.Code, policy?.MinimumVersion, policy?.LatestVersion, policy?.EnforceMinimum ?? false, policy?.UpdateUrl, policy?.ReleaseNotes, policy?.Sha256);
    }
    public async Task<VersionPolicyDto> Update(Guid applicationId, UpdateVersionPolicy request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator); ClientVersionRules.Validate(request, options.AllowLoopbackHttp);
        await using var tx = await db.Database.BeginTransactionAsync();
        // Policy changes serialize with activation/heartbeat application readers.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {applicationId} FOR UPDATE");
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId)) throw new RuleException("NOT_FOUND", 404);
        var policy = await db.VersionPolicies.SingleOrDefaultAsync(x => x.ApplicationId == applicationId);
        var before = Dto(policy ?? new() { ApplicationId = applicationId });
        if (policy is null) { policy = new() { ApplicationId = applicationId }; db.VersionPolicies.Add(policy); }
        policy.MinimumVersion = request.MinimumVersion; policy.LatestVersion = request.LatestVersion; policy.EnforceMinimum = request.EnforceMinimum;
        policy.UpdateUrl = request.UpdateUrl; policy.ReleaseNotes = request.ReleaseNotes; policy.Sha256 = request.Sha256?.ToLowerInvariant();
        var after = Dto(policy);
        db.AuditLogs.Add(new() { ActorId = actor.Id, Action = "application.version-policy.update", TargetId = applicationId, ApplicationId = applicationId,
            Changes = JsonSerializer.Serialize(new { before, after }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = clock.GetUtcNow(), TraceId = actor.TraceId });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return after;
    }
    private static VersionPolicyDto Dto(ApplicationVersionPolicy policy) => new(policy.ApplicationId, policy.MinimumVersion, policy.LatestVersion, policy.EnforceMinimum, policy.UpdateUrl, policy.ReleaseNotes, policy.Sha256);
}
