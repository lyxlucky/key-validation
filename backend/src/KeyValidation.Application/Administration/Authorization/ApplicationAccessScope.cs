using KeyValidation.Domain;

namespace KeyValidation.Application;
// One instance belongs to one request. A scope without an administrative request
// is unrestricted so software clients, migrations and background jobs retain their behavior.
public sealed class ApplicationAccessScope
{
    private Func<string, CancellationToken, Task>? recheckWrite;
    private readonly HashSet<string> requiredWritePermissions = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> orderLicenses = [];
    private readonly HashSet<Guid> orderBatches = [];
    private readonly HashSet<Guid> orderBindings = [];
    private readonly HashSet<Guid> orderChannels = [];
    public bool IsAdministrativeRequest { get; private set; }
    public bool IsAdministrator { get; private set; }
    public bool AllApplications { get; private set; } = true;
    public Guid[] ApplicationIds { get; private set; } = [];
    public Guid? ChannelId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string[] Permissions { get; private set; } = ApplicationPermissions.All.ToArray();
    public IReadOnlyCollection<string> RequiredWritePermissions => requiredWritePermissions;

    public void Initialize(AccountAccessDto access, bool isAdministrator, Func<string, CancellationToken, Task> recheck)
    {
        IsAdministrativeRequest = true;
        IsAdministrator = access.Permissions.Contains(ApplicationPermissions.Administrator, StringComparer.Ordinal);
        AllApplications = access.AllApplications;
        ApplicationIds = access.ApplicationIds.ToArray();
        ChannelId = access.ChannelId;
        Permissions = access.Permissions.ToArray();
        recheckWrite = recheck;
    }

    public void RequireGlobalPlanManagement(bool write = false)
    {
        if (write)
            RequireWrite(ApplicationPermissions.Administrator);
        else
            Require(ApplicationPermissions.Administrator);
        if (IsAdministrativeRequest && (!AllApplications || ChannelId != null || CustomerId != null))
            throw new RuleException("FORBIDDEN", 403);
    }

    public bool Includes(Guid applicationId) => AllApplications || ApplicationIds.Contains(applicationId);
    public bool IncludesChannel(Guid? channelId) => ChannelId is null || ChannelId == channelId;
    public void RestrictToCustomer(Guid customerId) => CustomerId = customerId;
    // Only the application orchestration can authorize these concrete resources.
    // The context still verifies scope, current grants and the actor stamp.
    internal void AuthorizeOrderLicense(Guid id)
    {
        RequireWrite(ApplicationPermissions.ManageOrders);
        orderLicenses.Add(id);
    }

    internal void AuthorizeOrderBatch(Guid id)
    {
        RequireWrite(ApplicationPermissions.ManageOrders);
        orderBatches.Add(id);
    }

    internal void AuthorizeOrderBinding(Guid id)
    {
        RequireWrite(ApplicationPermissions.ManageOrders);
        orderBindings.Add(id);
    }

    internal void AuthorizeOrderChannel(Guid id)
    {
        RequireWrite(ApplicationPermissions.ManageOrders);
        orderChannels.Add(id);
    }

    public bool IsOrderLicense(Guid id) => orderLicenses.Contains(id);
    public bool IsOrderBatch(Guid id) => orderBatches.Contains(id);
    public bool IsOrderBinding(Guid id) => orderBindings.Contains(id);
    public bool IsOrderChannel(Guid id) => orderChannels.Contains(id);
    public bool Allows(string permission) => !IsAdministrativeRequest || Permissions.Contains(permission, StringComparer.Ordinal);
    public void Require(string permission)
    {
        if (!Allows(permission))
            throw new RuleException("FORBIDDEN", 403);
    }

    public void RequireWrite(string permission)
    {
        Require(permission);
        // Record the requested action before acquiring resource locks. An
        // idempotent action still requires fresh authority even if no row changes.
        if (IsAdministrativeRequest)
            requiredWritePermissions.Add(permission);
    }

    public Task RecheckWrite(string permission, CancellationToken cancellationToken = default)
    {
        Require(permission);
        return recheckWrite?.Invoke(permission, cancellationToken) ?? Task.CompletedTask;
    }

    public AccountAccessDto ToDto() => new(Permissions.ToArray(), AllApplications, ApplicationIds.ToArray(), ChannelId);
}
