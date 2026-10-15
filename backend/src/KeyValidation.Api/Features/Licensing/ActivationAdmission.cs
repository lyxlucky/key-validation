using System.Threading.RateLimiting;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;
public sealed class ActivationAdmission : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    public bool TryAcquire(string ip, string? appCode)
    {
        using var lease = limiter.AttemptAcquire(ip + ":" + ApplicationCode.Normalize(appCode));
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}

public sealed class ActivationAdmissionFilter(ActivationAdmission admission) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var appCode = context.Arguments.FirstOrDefault(x => x is ActivateLicense or ActivateTrial) switch
        {
            ActivateLicense request => request.AppCode,
            ActivateTrial request => request.AppCode,
            _ => throw new RuleException("VALIDATION_ERROR", 400)};
        if (!admission.TryAcquire(context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", appCode))
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            throw new RuleException("RATE_LIMITED", 429);
        }

        return next(context);
    }
}
