using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KeyValidation.Api;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record PasswordConfirmationRequest(string Password);
public record TotpCodeRequest(string Code, bool UseRecoveryCode = false);
public record TwoFactorLoginRequest(string ChallengeToken, string Code, bool UseRecoveryCode = false);
public record TwoFactorChallengeDto(bool RequiresTwoFactor, string ChallengeToken);
public record AccountSecurityDto(bool MustChangePassword, bool TwoFactorEnabled, int RecoveryCodesRemaining, DateTimeOffset? VerificationExpiresAt);
public record TotpSetupDto(string SharedKey, string AuthenticatorUri);
public record TotpConfirmationDto(string[] RecoveryCodes, UserDto User);
public record RecoveryCodesDto(string[] RecoveryCodes);

public sealed class AccountSecurityService(
    AppDbContext db, UserManager<AdminUser> users, SignInManager<AdminUser> signIn,
    IDataProtectionProvider protection, TimeProvider clock, IHttpContextAccessor contexts)
{
    private const string VerificationProperty = "two-factor-verified";
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AbsoluteSessionLifetime = TimeSpan.FromHours(8);
    private readonly IDataProtector secretProtector = protection.CreateProtector("KeyValidation.AccountSecurity.Totp.v1");
    private readonly IDataProtector challengeProtector = protection.CreateProtector("KeyValidation.AccountSecurity.LoginChallenge.v1");
    private sealed record LoginChallenge(string UserId, string SecurityStamp, Guid Id, DateTimeOffset ExpiresAt);

    public async Task<object> Login(LoginRequest request, HttpContext context)
    {
        await using var tx = await BeginChange();
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > 128 || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
            throw await RejectLogin(tx, context, null, "password", "invalid-input", "VALIDATION_ERROR", 400);
        var user = await users.FindByNameAsync(request.UserName);
        if (user is null || user.DisabledAt is not null || IsLocked(user))
            throw await RejectLogin(tx, context, user, "password", user is null ? "credentials" : user.DisabledAt is not null ? "disabled" : "locked", "LOGIN_FAILED", 401);
        if (!await users.CheckPasswordAsync(user, request.Password)) await Fail(tx, user, "LOGIN_FAILED", 401, context, "password", "credentials");
        if (user.TwoFactorEnabled)
        {
            // A password alone cannot reset failed second factors or establish a session.
            var challenge = new LoginChallenge(user.Id, user.SecurityStamp!, Guid.NewGuid(), clock.GetUtcNow().Add(ChallengeLifetime));
            user.LoginChallengeId = challenge.Id;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return new TwoFactorChallengeDto(true, challengeProtector.Protect(JsonSerializer.Serialize(challenge)));
        }
        ClearFailures(user);
        user.LoginChallengeId = null;
        await Complete(tx, context, user, "security.login", "password");
        await SignIn(context, user, fresh: true);
        return await Dto(user);
    }

    public async Task<UserDto> CompleteLogin(TwoFactorLoginRequest request, HttpContext context)
    {
        await using var tx = await BeginChange();
        var method = request.UseRecoveryCode ? "recovery-code" : "totp";
        LoginChallenge challenge;
        try { challenge = ReadChallenge(request.ChallengeToken); }
        catch (RuleException) { throw await RejectLogin(tx, context, null, method, "challenge", "TWO_FACTOR_CHALLENGE_INVALID", 400); }
        var user = await users.FindByIdAsync(challenge.UserId);
        if (user is null || user.DisabledAt is not null || !user.TwoFactorEnabled ||
            user.SecurityStamp != challenge.SecurityStamp || user.LoginChallengeId != challenge.Id || clock.GetUtcNow() >= challenge.ExpiresAt)
            throw await RejectLogin(tx, context, user, method, "challenge", "TWO_FACTOR_CHALLENGE_INVALID", 400);
        if (IsLocked(user) || !await ConsumeCode(user, request.Code, request.UseRecoveryCode)) await Fail(tx, user, "TWO_FACTOR_INVALID", 400, context, method, IsLocked(user) ? "locked" : "second-factor");
        user.LoginChallengeId = null;
        ClearFailures(user);
        await Complete(tx, context, user, "security.login-two-factor", method);
        await SignIn(context, user, fresh: true, verified: true);
        return await Dto(user);
    }

    public async Task<UserDto> Me(HttpContext context)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new RuleException("UNAUTHENTICATED", 401);
        return await Dto(user);
    }

    public async Task Logout(HttpContext context)
    {
        await using var tx = await BeginChange();
        var user = await CurrentUser(context, allowPasswordChange: true);
        await Complete(tx, context, user, "security.logout", "session");
        await signIn.SignOutAsync();
    }

    public async Task<AccountSecurityDto> Status(HttpContext context)
    {
        var user = await users.GetUserAsync(context.User) ?? throw new RuleException("UNAUTHENTICATED", 401);
        return new(user.MustChangePassword, user.TwoFactorEnabled,
            await db.AdminRecoveryCodes.CountAsync(x => x.UserId == user.Id),
            user.TwoFactorEnabled ? await VerificationExpiry(context) : null);
    }

    public async Task<UserDto> ChangePassword(ChangePasswordRequest request, HttpContext context)
    {
        ValidatePassword(request.CurrentPassword);
        ValidatePassword(request.NewPassword);
        await using var tx = await BeginChange();
        var user = await CurrentUser(context, allowPasswordChange: true);
        await CheckPassword(tx, user, request.CurrentPassword);
        if (request.CurrentPassword == request.NewPassword) throw new RuleException("VALIDATION_ERROR", 400);
        Check(await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        user.MustChangePassword = false;
        ClearPending(user);
        user.LoginChallengeId = null;
        ClearFailures(user);
        await Complete(tx, context, user, "security.password");
        await SignIn(context, user);
        return await Dto(user);
    }

    public async Task<TotpSetupDto> Setup(PasswordConfirmationRequest request, HttpContext context)
    {
        ValidatePassword(request.Password);
        await using var tx = await BeginChange();
        var user = await CurrentUser(context);
        if (user.TwoFactorEnabled) throw new RuleException("TWO_FACTOR_ALREADY_ENABLED", 409);
        await CheckPassword(tx, user, request.Password);
        var secret = Totp.GenerateKey();
        user.ProtectedPendingTotpSecret = secretProtector.Protect(secret);
        user.PendingTotpExpiresAt = clock.GetUtcNow().Add(ChallengeLifetime);
        user.PendingTotpSecurityStamp = user.SecurityStamp;
        await Complete(tx, context, user, "security.two-factor-setup");
        var label = Uri.EscapeDataString("KeyValidation:" + user.UserName);
        return new(secret, $"otpauth://totp/{label}?secret={secret}&issuer=KeyValidation&algorithm=SHA1&digits=6&period=30");
    }

    public async Task<TotpConfirmationDto> Confirm(TotpCodeRequest request, HttpContext context)
    {
        await using var tx = await BeginChange();
        var user = await CurrentUser(context);
        if (user.TwoFactorEnabled) throw new RuleException("TWO_FACTOR_ALREADY_ENABLED", 409);
        if (IsLocked(user) || user.ProtectedPendingTotpSecret is null || user.PendingTotpExpiresAt is null ||
            clock.GetUtcNow() >= user.PendingTotpExpiresAt || user.PendingTotpSecurityStamp != user.SecurityStamp)
            await Fail(tx, user, "TWO_FACTOR_INVALID");
        if (!Totp.Verify(secretProtector.Unprotect(user.ProtectedPendingTotpSecret!), request.Code, clock.GetUtcNow(), null, out var step))
            await Fail(tx, user, "TWO_FACTOR_INVALID");
        user.ProtectedTotpSecret = user.ProtectedPendingTotpSecret;
        user.TwoFactorEnabled = true;
        user.LastTotpStep = step;
        ClearPending(user);
        ClearFailures(user);
        var codes = await ReplaceRecoveryCodes(user);
        Check(await users.UpdateSecurityStampAsync(user));
        user.LoginChallengeId = null;
        await Complete(tx, context, user, "security.two-factor-enable");
        await SignIn(context, user, verified: true);
        return new(codes, await Dto(user));
    }

    public async Task<UserDto> Disable(PasswordConfirmationRequest request, HttpContext context)
    {
        ValidatePassword(request.Password);
        await using var tx = await BeginChange();
        var user = await CurrentUser(context);
        if (!user.TwoFactorEnabled) throw new RuleException("TWO_FACTOR_NOT_ENABLED", 409);
        await CheckPassword(tx, user, request.Password);
        ClearFailures(user);
        user.TwoFactorEnabled = false;
        user.ProtectedTotpSecret = null;
        user.LastTotpStep = null;
        user.LoginChallengeId = null;
        ClearPending(user);
        db.AdminRecoveryCodes.RemoveRange(await db.AdminRecoveryCodes.Where(x => x.UserId == user.Id).ToListAsync());
        Check(await users.UpdateSecurityStampAsync(user));
        await Complete(tx, context, user, "security.two-factor-disable");
        await SignIn(context, user);
        return await Dto(user);
    }

    public async Task<RecoveryCodesDto> Regenerate(PasswordConfirmationRequest request, HttpContext context)
    {
        ValidatePassword(request.Password);
        await using var tx = await BeginChange();
        var user = await CurrentUser(context);
        if (!user.TwoFactorEnabled) throw new RuleException("TWO_FACTOR_NOT_ENABLED", 409);
        await CheckPassword(tx, user, request.Password);
        ClearFailures(user);
        var codes = await ReplaceRecoveryCodes(user);
        await Complete(tx, context, user, "security.recovery-codes");
        await SignIn(context, user);
        return new(codes);
    }

    public async Task Verify(TotpCodeRequest request, HttpContext context)
    {
        await using var tx = await BeginChange();
        var user = await CurrentUser(context, allowPasswordChange: true);
        await CheckCode(tx, user, request.Code, request.UseRecoveryCode);
        await Complete(tx, context, user, "security.verify");
        await SignIn(context, user, verified: true);
    }

    public async Task EnforceAccess(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin") || context.User.Identity?.IsAuthenticated != true) return;
        var path = (context.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
        // These endpoints establish authentication or obtain the CSRF token needed for it.
        if (path is "/api/admin/auth/login" or "/api/admin/auth/two-factor" or "/api/admin/auth/csrf") return;
        var user = await CurrentUser(context, allowPasswordChange: true);
        var read = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        if (user.MustChangePassword)
        {
            var allowed = read && path is "/api/admin/auth/me" or "/api/admin/auth/csrf" or "/api/admin/security" ||
                HttpMethods.IsPost(context.Request.Method) && path is "/api/admin/auth/logout" or "/api/admin/security/password" or "/api/admin/security/verify";
            if (!allowed) throw new RuleException("PASSWORD_CHANGE_REQUIRED", 403);
        }
    }

    public async Task RecheckAccountMutation(HttpContext context)
    {
        // AdminAccountService calls this after taking the shared lock and clearing
        // tracked state. A queued request may outlive its password, role or session.
        await CurrentUser(context);
    }

    private async Task<IDbContextTransaction> BeginChange()
    {
        var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // Same namespace as AdminAccountService and CLI. This also serializes role/reset
            // changes with code use, so a request cannot authenticate stale account state.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
            db.ChangeTracker.Clear();
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    private async Task<AdminUser> CurrentUser(HttpContext context, bool allowPasswordChange = false)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is null || user.DisabledAt is not null || user.SecurityStamp != context.User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType))
            throw new RuleException("UNAUTHENTICATED", 401);
        if (user.TwoFactorEnabled) await RequireSessionVerification(context);
        if (user.MustChangePassword && !allowPasswordChange) throw new RuleException("PASSWORD_CHANGE_REQUIRED", 403);
        return user;
    }

    private bool IsLocked(AdminUser user) => user.LockoutEnabled && user.LockoutEnd is { } end && end > clock.GetUtcNow();
    private static void ClearFailures(AdminUser user) { user.AccessFailedCount = 0; user.LockoutEnd = null; }
    internal static void ClearPending(AdminUser user)
    {
        user.ProtectedPendingTotpSecret = null;
        user.PendingTotpExpiresAt = null;
        user.PendingTotpSecurityStamp = null;
    }

    private async Task CheckPassword(IDbContextTransaction tx, AdminUser user, string password)
    {
        if (IsLocked(user) || !await users.CheckPasswordAsync(user, password)) await Fail(tx, user, "CURRENT_PASSWORD_INVALID");
    }

    private async Task CheckCode(IDbContextTransaction tx, AdminUser user, string code, bool recovery)
    {
        if (IsLocked(user) || !await ConsumeCode(user, code, recovery)) await Fail(tx, user, "TWO_FACTOR_INVALID");
        ClearFailures(user);
    }

    private async Task<bool> ConsumeCode(AdminUser user, string code, bool recovery)
    {
        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(code) || code.Length > 128) return false;
        if (recovery)
        {
            var digest = RecoveryDigest(code.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant());
            var stored = await db.AdminRecoveryCodes.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Digest == digest);
            if (stored is null) return false;
            db.AdminRecoveryCodes.Remove(stored);
            return true;
        }
        if (user.ProtectedTotpSecret is null || !Totp.Verify(secretProtector.Unprotect(user.ProtectedTotpSecret), code, clock.GetUtcNow(), user.LastTotpStep, out var step)) return false;
        user.LastTotpStep = step;
        return true;
    }

    private async Task<string[]> ReplaceRecoveryCodes(AdminUser user)
    {
        db.AdminRecoveryCodes.RemoveRange(await db.AdminRecoveryCodes.Where(x => x.UserId == user.Id).ToListAsync());
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        db.AdminRecoveryCodes.AddRange(codes.Select(code => new AdminRecoveryCode { UserId = user.Id, Digest = RecoveryDigest(code) }));
        return codes;
    }

    private static string RecoveryDigest(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private async Task Fail(IDbContextTransaction tx, AdminUser user, string code, int status = 400, HttpContext? loginContext = null, string? loginMethod = null, string? errorCategory = null)
    {
        // Commit failure accounting before returning the error; rolling back would disable lockout.
        if (!IsLocked(user) && user.LockoutEnabled)
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= users.Options.Lockout.MaxFailedAccessAttempts)
            {
                user.AccessFailedCount = 0;
                user.LockoutEnd = clock.GetUtcNow().Add(users.Options.Lockout.DefaultLockoutTimeSpan);
            }
        }
        if (loginContext is not null) AuthenticationAudit(loginContext, user, "security.login-failed", loginMethod!, "failed", errorCategory);
        else if (contexts.HttpContext is { } context)
        {
            db.AuditLogs.Add(new() { ActorId = user.Id, ActorName = user.UserName, TargetId = Guid.Parse(user.Id), TargetType = "account", TargetName = user.UserName,
                Action = "security.verification-failed", Category = "operation", Result = "failed", ErrorCategory = "verification", CreatedAt = clock.GetUtcNow(), TraceId = context.TraceIdentifier });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        throw new RuleException(code, status);
    }

    private async Task Complete(IDbContextTransaction tx, HttpContext context, AdminUser user, string action, string? method = null)
    {
        if (method is not null) AuthenticationAudit(context, user, action, method, "succeeded", null);
        else db.AuditLogs.Add(new AdminAuditLog
        {
            ActorId = user.Id, ActorName = user.UserName, TargetId = Guid.Parse(user.Id), TargetType = "account", TargetName = user.UserName, Action = action, Changes = "{}",
            CreatedAt = clock.GetUtcNow(), TraceId = context.TraceIdentifier
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<RuleException> RejectLogin(IDbContextTransaction tx, HttpContext context, AdminUser? user, string method, string errorCategory, string code, int status)
    {
        AuthenticationAudit(context, user, "security.login-failed", method, "failed", errorCategory);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new RuleException(code, status);
    }

    private void AuthenticationAudit(HttpContext context, AdminUser? user, string action, string method, string result, string? errorCategory)
    {
        // ForwardedHeaders has already validated any trusted proxy. Never read
        // forwarding headers here, and never persist submitted credentials/names.
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        db.AuditLogs.Add(new() { ActorId = user?.Id ?? "anonymous", ActorName = user?.UserName,
            TargetId = user is not null && Guid.TryParse(user.Id, out var id) ? id : Guid.Empty, TargetType = "account", TargetName = user?.UserName,
            Action = action, Category = "login", Result = result, SourceIp = address?.ToString(), Method = method, ErrorCategory = errorCategory,
            CreatedAt = clock.GetUtcNow(), TraceId = context.TraceIdentifier });
    }

    private LoginChallenge ReadChallenge(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096) throw new RuleException("TWO_FACTOR_CHALLENGE_INVALID", 400);
        try
        {
            return JsonSerializer.Deserialize<LoginChallenge>(challengeProtector.Unprotect(value)) ?? throw new RuleException("TWO_FACTOR_CHALLENGE_INVALID", 400);
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { throw new RuleException("TWO_FACTOR_CHALLENGE_INVALID", 400); }
    }

    private async Task<DateTimeOffset?> VerificationExpiry(HttpContext context)
    {
        var auth = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var properties = auth.Properties;
        var now = clock.GetUtcNow();
        if (!auth.Succeeded || properties?.ExpiresUtc is not { } expires || now >= expires ||
            !properties.Items.TryGetValue("absolute-issued", out var issuedRaw) ||
            !long.TryParse(issuedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issued) ||
            !properties.Items.TryGetValue(VerificationProperty, out var verifiedRaw) ||
            !long.TryParse(verifiedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var verified) ||
            issued < 0 || verified < issued || verified > now.ToUnixTimeSeconds()) return null;
        var started = DateTimeOffset.FromUnixTimeSeconds(issued);
        if (now - started >= AbsoluteSessionLifetime) return null;
        // Keep the cookie's current idle deadline and the original eight-hour cap,
        // including when an in-flight write waits for the account-change lock.
        var absoluteExpiry = started.Add(AbsoluteSessionLifetime);
        return expires < absoluteExpiry ? expires : absoluteExpiry;
    }

    private async Task RequireSessionVerification(HttpContext context)
    {
        if (await VerificationExpiry(context) is not null) return;
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        throw new RuleException("UNAUTHENTICATED", 401);
    }

    private async Task SignIn(HttpContext context, AdminUser user, bool fresh = false, bool verified = false)
    {
        var properties = fresh ? new AuthenticationProperties() :
            (await context.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Properties ?? new AuthenticationProperties();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        // Principal creation can cross a second boundary; a fresh session and its
        // verification must use the same origin before the cookie signing event.
        if (fresh) properties.Items["absolute-issued"] = timestamp;
        if (verified) properties.Items[VerificationProperty] = timestamp;
        if (!user.TwoFactorEnabled) properties.Items.Remove(VerificationProperty);
        await context.SignInAsync(IdentityConstants.ApplicationScheme, await signIn.CreateUserPrincipalAsync(user), properties);
    }

    private async Task<UserDto> Dto(AdminUser user) => new(user.Id, user.UserName!, (await users.GetRolesAsync(user)).ToArray(), user.MustChangePassword, user.TwoFactorEnabled);
    private static void ValidatePassword(string password) { if (string.IsNullOrEmpty(password) || password.Length > 1024) throw new RuleException("VALIDATION_ERROR", 400); }
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new RuleException("VALIDATION_ERROR", 400); }
}
