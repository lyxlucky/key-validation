using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

internal static class RequestActor
{
    public static Actor FromContext(HttpContext context) => new(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401),
        context.TraceIdentifier);
}
