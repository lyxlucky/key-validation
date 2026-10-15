using KeyValidation.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/auth")]
[Produces("application/json")]
[Tags("Admin authentication")]
public sealed class AuthenticationController(IAntiforgery iAntiforgery, AccountSecurityService accountSecurityService) : ControllerBase
{
    private readonly IAntiforgery _iAntiforgery = iAntiforgery;
    private readonly AccountSecurityService _accountSecurityService = accountSecurityService;
    [HttpGet("csrf")]
    public CsrfDto GetCsrf() => new CsrfDto(_iAntiforgery.GetAndStoreTokens(HttpContext).RequestToken!);
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(TwoFactorChallengeDto), 200)]
    public async Task<IActionResult> PostLogin([FromBody] LoginRequest request)
    {
        var result = await _accountSecurityService.Login(request, HttpContext);
        return result is UserDto user ? Ok(user) : Ok((TwoFactorChallengeDto)result);
    }

    [HttpPost("two-factor")]
    [EnableRateLimiting("login")]
    public Task<UserDto> PostTwoFactor([FromBody] TwoFactorLoginRequest request) => _accountSecurityService.CompleteLogin(request, HttpContext);
    [HttpGet("me")]
    [Authorize(Policy = "Read")]
    public Task<UserDto> GetMe() => _accountSecurityService.Me(HttpContext);
    [HttpPost("logout")]
    [Authorize(Policy = "Read")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLogout()
    {
        await _accountSecurityService.Logout(HttpContext);
        return NoContent();
    }
}
