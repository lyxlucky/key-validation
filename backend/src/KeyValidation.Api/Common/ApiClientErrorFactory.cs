using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace KeyValidation.Api;

internal sealed class ApiClientErrorFactory : IClientErrorFactory
{
    public IActionResult GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
    {
        var status = clientError.StatusCode ?? 500;
        return new ApiErrorResult(status, status switch
        {
            400 or 415 => "VALIDATION_ERROR",
            401 => "UNAUTHENTICATED",
            403 => "FORBIDDEN",
            404 => "NOT_FOUND",
            _ => "REQUEST_FAILED"
        });
    }
}
