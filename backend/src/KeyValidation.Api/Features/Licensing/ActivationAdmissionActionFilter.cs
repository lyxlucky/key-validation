using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KeyValidation.Api;
public sealed class ActivationAdmissionActionFilter(ActivationAdmission admission) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var appCode = context.ActionArguments.Values.FirstOrDefault(value => value is ActivateLicense or ActivateTrial) switch
        {
            ActivateLicense request => request.AppCode,
            ActivateTrial request => request.AppCode,
            _ => throw new RuleException("VALIDATION_ERROR", 400)};
        if (!admission.TryAcquire(context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", appCode))
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";
            throw new RuleException("RATE_LIMITED", 429);
        }

        await next();
    }
}
