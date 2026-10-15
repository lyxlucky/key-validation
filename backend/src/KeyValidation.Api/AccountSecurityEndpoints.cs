using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace KeyValidation.Api;

public static class AccountSecurityEndpoints
{
    public static void MapAccountSecurityApi(this WebApplication app)
    {
        var auth = app.MapGroup("/api/admin/auth").WithTags("Admin authentication");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery anti) => new CsrfDto(anti.GetAndStoreTokens(context).RequestToken!));
        auth.MapPost("/login", async Task<Results<Ok<UserDto>, Ok<TwoFactorChallengeDto>>> (LoginRequest request, HttpContext context, AccountSecurityService service) =>
        {
            var result = await service.Login(request, context);
            return result is UserDto user ? TypedResults.Ok(user) : TypedResults.Ok((TwoFactorChallengeDto)result);
        }).RequireRateLimiting("login");
        auth.MapPost("/two-factor", (TwoFactorLoginRequest request, HttpContext context, AccountSecurityService service) => service.CompleteLogin(request, context)).RequireRateLimiting("login");
        auth.MapGet("/me", (HttpContext context, AccountSecurityService service) => service.Me(context)).RequireAuthorization("Read");
        auth.MapPost("/logout", async (HttpContext context, AccountSecurityService service) => { await service.Logout(context); return TypedResults.NoContent(); }).RequireAuthorization("Read");

        var security = app.MapGroup("/api/admin/security").WithTags("Account security").RequireAuthorization("Read");
        security.MapGet("", (HttpContext context, AccountSecurityService service) => service.Status(context));
        security.MapPost("/password", (ChangePasswordRequest request, HttpContext context, AccountSecurityService service) => service.ChangePassword(request, context)).RequireRateLimiting("login");
        security.MapPost("/two-factor/setup", (PasswordConfirmationRequest request, HttpContext context, AccountSecurityService service) => service.Setup(request, context)).RequireRateLimiting("login");
        security.MapPost("/two-factor/confirm", (TotpCodeRequest request, HttpContext context, AccountSecurityService service) => service.Confirm(request, context)).RequireRateLimiting("login");
        security.MapPost("/two-factor/disable", (PasswordConfirmationRequest request, HttpContext context, AccountSecurityService service) => service.Disable(request, context)).RequireRateLimiting("login");
        security.MapPost("/two-factor/recovery-codes", (PasswordConfirmationRequest request, HttpContext context, AccountSecurityService service) => service.Regenerate(request, context)).RequireRateLimiting("login");
        security.MapPost("/verify", async (TotpCodeRequest request, HttpContext context, AccountSecurityService service) => { await service.Verify(request, context); return TypedResults.NoContent(); }).RequireRateLimiting("login");
    }
}
