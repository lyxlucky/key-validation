using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class CustomerService(ICommerceData db, ApplicationAccessScope access, CommerceWorkflow workflow)
{
    public async Task<PagedResult<CustomerDto>> List(int page, int pageSize, Guid? applicationId, Guid? channelId, string? search)
    {
        access.Require(ApplicationPermissions.ManageCustomers); CommerceWorkflow.Page(page, pageSize);
        search = CommerceWorkflow.Optional(search, 254);
        var query = db.Customers.AsNoTracking().Where(x => (applicationId == null || x.ApplicationId == applicationId) && (channelId == null || x.ChannelId == channelId));
        if (search != null) query = query.Where(x => x.Name != null && x.Name.Contains(search) || x.Email != null && x.Email.Contains(search) || x.Phone != null && x.Phone.Contains(search));
        var total = await query.CountAsync(); var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(rows.Select(CommerceWorkflow.CustomerDto).ToArray(), total, page, pageSize);
    }
    public async Task<CustomerDto> Get(Guid id)
    {
        access.Require(ApplicationPermissions.ManageCustomers);
        return CommerceWorkflow.CustomerDto(await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404));
    }
    public async Task<CustomerDto> Create(CreateCustomer request, Actor actor, bool requireRequestId = false)
    {
        access.RequireWrite(ApplicationPermissions.ManageCustomers);
        var requestId = request.RequestId.GetValueOrDefault();
        var name = CommerceWorkflow.Text(request.Name); var email = CommerceWorkflow.Optional(request.Email, 254); var phone = CommerceWorkflow.Optional(request.Phone, 40); var note = CommerceWorkflow.Optional(request.Note, 500);
        CommerceWorkflow.Require(request.ApplicationId != Guid.Empty && (!requireRequestId || requestId != Guid.Empty));
        var channel = request.ChannelId ?? access.ChannelId;
        if (!access.IncludesChannel(channel) || !access.Includes(request.ApplicationId)) throw new RuleException("NOT_FOUND", 404);
        var fingerprint = CommerceWorkflow.Fingerprint(new { request.ApplicationId, channel, name, email, phone, note });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (requestId != Guid.Empty)
        {
            await workflow.RequestLock(actor.Id, "customer.create", requestId);
            if (await workflow.Receipt(actor, "customer.create", requestId, fingerprint, ApplicationPermissions.ManageCustomers) is { } previous) return await Get(previous);
        }
        await workflow.LockApplication(request.ApplicationId); await workflow.LockChannel(channel, request.ApplicationId);
        var now = workflow.Now;
        var customer = new Customer { ApplicationId = request.ApplicationId, ChannelId = channel, Name = name, Email = email, Phone = phone, Note = note, CreatedAt = now, UpdatedAt = now };
        db.Customers.Add(customer);
        if (requestId != Guid.Empty) workflow.Record(actor, "customer.create", requestId, fingerprint, customer.Id);
        workflow.Audit(actor, "customer.create", customer.Id, customer.ApplicationId, customer.ChannelId, new { customer.ApplicationId, customer.ChannelId }, Label(customer));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return CommerceWorkflow.CustomerDto(customer);
    }
    public async Task<CustomerDto> Update(Guid id, UpdateCustomer request, Actor actor, Guid requestId = default, bool requireRequestId = false)
    {
        access.RequireWrite(ApplicationPermissions.ManageCustomers);
        CommerceWorkflow.Require(!requireRequestId || requestId != Guid.Empty);
        var name = CommerceWorkflow.Text(request.Name); var email = CommerceWorkflow.Optional(request.Email, 254); var phone = CommerceWorkflow.Optional(request.Phone, 40); var note = CommerceWorkflow.Optional(request.Note, 500);
        var fingerprint = CommerceWorkflow.Fingerprint(new { id, name, email, phone, note });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (requestId != Guid.Empty)
        {
            await workflow.RequestLock(actor.Id, "customer.update", requestId);
            if (await workflow.Receipt(actor, "customer.update", requestId, fingerprint, ApplicationPermissions.ManageCustomers) is { } previous) return await Get(previous);
        }
        var location = await Location(id);
        await workflow.LockApplication(location.ApplicationId, false); await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        var customer = await workflow.LockCustomer(id, false);
        if (customer.AnonymizedAt != null) throw new RuleException("CUSTOMER_ANONYMIZED", 409);
        customer.Name = name; customer.Email = email; customer.Phone = phone; customer.Note = note; customer.UpdatedAt = workflow.Now;
        workflow.Audit(actor, "customer.update", id, customer.ApplicationId, customer.ChannelId, new { fields = new[] { "name", "email", "phone", "note" } }, Label(customer));
        if (requestId != Guid.Empty) workflow.Record(actor, "customer.update", requestId, fingerprint, id);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return CommerceWorkflow.CustomerDto(customer);
    }
    public async Task<CustomerDto> SetAccess(Guid id, string action, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageCustomers); var location = await Location(id);
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.LockApplication(location.ApplicationId, false); await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        var customer = await workflow.LockCustomer(id, false);
        // Customer/session revocation shares the existing authorization lock.
        // Resource locks precede it, matching business writes and avoiding inversion.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
        await access.RecheckWrite(ApplicationPermissions.ManageCustomers);
        var now = workflow.Now;
        if (action == "enable")
        {
            if (customer.AnonymizedAt != null) throw new RuleException("CUSTOMER_ANONYMIZED", 409);
            customer.DisabledAt = null;
        }
        else
        {
            customer.DisabledAt ??= now; customer.AccessVersion = Guid.NewGuid(); customer.AccessCodeDigest = null;
            if (action == "anonymize") { customer.Name = null; customer.Email = null; customer.Phone = null; customer.Note = null; customer.AnonymizedAt ??= now; }
            await RevokeSessions(id, now);
        }
        customer.UpdatedAt = now;
        workflow.Audit(actor, "customer." + action, id, customer.ApplicationId, customer.ChannelId, new { disabled = customer.DisabledAt != null, anonymized = customer.AnonymizedAt != null }, Label(customer));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return CommerceWorkflow.CustomerDto(customer);
    }
    public async Task<CustomerAccessCodeDto> RotateAccessCode(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageCustomers); var location = await Location(id);
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.LockApplication(location.ApplicationId); await workflow.LockChannel(location.ChannelId, location.ApplicationId);
        var customer = await workflow.LockCustomer(id);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)"); await access.RecheckWrite(ApplicationPermissions.ManageCustomers);
        var code = CommerceWorkflow.NewSecret("kgc_"); customer.AccessCodeDigest = CommerceWorkflow.Digest(code); customer.AccessVersion = Guid.NewGuid(); customer.UpdatedAt = workflow.Now;
        await RevokeSessions(id, workflow.Now);
        workflow.Audit(actor, "customer.access-code.rotate", id, customer.ApplicationId, customer.ChannelId, label: Label(customer));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(CommerceWorkflow.CustomerDto(customer), code);
    }
    public async Task<PagedResult<LicenseDto>> Licenses(Guid id, int page, int pageSize)
    {
        access.Require(ApplicationPermissions.ManageCustomers); CommerceWorkflow.Page(page, pageSize); await Location(id);
        var query = db.Licenses.AsNoTracking().Where(x => x.CustomerId == id); var count = await query.CountAsync();
        var licenses = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(licenses.Select(x => CommerceWorkflow.LicenseDto(x, workflow.Now)).ToArray(), count, page, pageSize);
    }
    private async Task<Customer> Location(Guid id) => await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    private async Task RevokeSessions(Guid id, DateTimeOffset now)
    {
        foreach (var session in await db.PortalSessions.Where(x => x.CustomerId == id && x.RevokedAt == null).ToArrayAsync()) session.RevokedAt = now;
    }
    private static string Label(Customer x) => "客户 " + x.Id.ToString("N")[..8];
}
