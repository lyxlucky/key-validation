using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public sealed class ClientVersionPolicyService(ILicensingData db, ApplicationAccessScope access, TimeProvider clock, ClientVersionOptions options)
{
    public async Task<VersionPolicyDto> Get(Guid applicationId)
    {
        if (!access.Allows(ApplicationPermissions.Administrator) && !access.Allows(ApplicationPermissions.ReadLicenses))
            throw new RuleException("FORBIDDEN", 403);
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId))
            throw new RuleException("NOT_FOUND", 404);
        return Dto(await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId) ?? new() { ApplicationId = applicationId });
    }

    public async Task<PublicVersionDto> Public(string applicationCode)
    {
        var code = ApplicationCode.Normalize(applicationCode);
        var app = await db.Applications.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code) ?? throw new RuleException("NOT_FOUND", 404);
        if (app.DisabledAt is not null)
            throw new RuleException("APPLICATION_DISABLED");
        var policy = await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == app.Id);
        return new(app.Code, policy?.MinimumVersion, policy?.LatestVersion, policy?.EnforceMinimum ?? false, policy?.UpdateUrl, policy?.ReleaseNotes, policy?.Sha256);
    }

    public async Task<VersionPolicyDto> Update(Guid applicationId, UpdateVersionPolicy request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        ClientVersionRules.Validate(request, options.AllowLoopbackHttp);
        await using var tx = await db.Database.BeginTransactionAsync();
        // Policy changes serialize with activation/heartbeat application readers.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_applications\" WHERE \"Id\" = {applicationId} FOR UPDATE");
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId))
            throw new RuleException("NOT_FOUND", 404);
        var policy = await db.VersionPolicies.SingleOrDefaultAsync(x => x.ApplicationId == applicationId);
        var before = Dto(policy ?? new() { ApplicationId = applicationId });
        if (policy is null)
        {
            policy = new()
            {
                ApplicationId = applicationId
            };
            db.VersionPolicies.Add(policy);
        }

        policy.MinimumVersion = request.MinimumVersion;
        policy.LatestVersion = request.LatestVersion;
        policy.EnforceMinimum = request.EnforceMinimum;
        policy.UpdateUrl = request.UpdateUrl;
        policy.ReleaseNotes = request.ReleaseNotes;
        policy.Sha256 = request.Sha256?.ToLowerInvariant();
        var after = Dto(policy);
        db.AuditLogs.Add(new() { ActorId = actor.Id, Action = "application.version-policy.update", TargetId = applicationId, ApplicationId = applicationId, Changes = JsonSerializer.Serialize(new { before, after }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = clock.GetUtcNow(), TraceId = actor.TraceId });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return after;
    }

    private static VersionPolicyDto Dto(ApplicationVersionPolicy policy) => new(policy.ApplicationId, policy.MinimumVersion, policy.LatestVersion, policy.EnforceMinimum, policy.UpdateUrl, policy.ReleaseNotes, policy.Sha256);
}
