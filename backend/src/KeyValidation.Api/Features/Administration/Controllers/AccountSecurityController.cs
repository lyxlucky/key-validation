using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/security")]
[Produces("application/json")]
[Tags("Account security")]
[Authorize(Policy = "Read")]
public sealed class AccountSecurityController(AccountSecurityService accountSecurityService) : ControllerBase
{
    private readonly AccountSecurityService _accountSecurityService = accountSecurityService;
    [HttpGet("")]
    public Task<AccountSecurityDto> GetIndex() => _accountSecurityService.Status(HttpContext);
    [HttpPost("password")]
    [EnableRateLimiting("login")]
    public Task<UserDto> PostPassword([FromBody] ChangePasswordRequest request) => _accountSecurityService.ChangePassword(request, HttpContext);
    [HttpPost("two-factor/setup")]
    [EnableRateLimiting("login")]
    public Task<TotpSetupDto> PostTwoFactorSetup([FromBody] PasswordConfirmationRequest request) => _accountSecurityService.Setup(request, HttpContext);
    [HttpPost("two-factor/confirm")]
    [EnableRateLimiting("login")]
    public Task<TotpConfirmationDto> PostTwoFactorConfirm([FromBody] TotpCodeRequest request) => _accountSecurityService.Confirm(request, HttpContext);
    [HttpPost("two-factor/disable")]
    [EnableRateLimiting("login")]
    public Task<UserDto> PostTwoFactorDisable([FromBody] PasswordConfirmationRequest request) => _accountSecurityService.Disable(request, HttpContext);
    [HttpPost("two-factor/recovery-codes")]
    [EnableRateLimiting("login")]
    public Task<RecoveryCodesDto> PostTwoFactorRecoveryCodes([FromBody] PasswordConfirmationRequest request) => _accountSecurityService.Regenerate(request, HttpContext);
    [HttpPost("verify")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostVerify([FromBody] TotpCodeRequest request)
    {
        await _accountSecurityService.Verify(request, HttpContext);
        return NoContent();
    }
}
