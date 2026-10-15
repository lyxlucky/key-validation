using Microsoft.AspNetCore.Mvc;

namespace KeyValidation.Api;
internal sealed class ApiErrorResult(int status = 400, string code = "VALIDATION_ERROR") : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context) => ApiErrors.Write(context.HttpContext, status, code);
}
