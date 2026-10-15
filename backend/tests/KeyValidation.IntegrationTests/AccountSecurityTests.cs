using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class AccountSecurityTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task FirstLoginRequiresPasswordChangeAndRefreshesOnlyCurrentSession()
    {
        var (name, id) = await CreateAccount();
        using var first = await fixture.Login(name);
        using var other = await fixture.Login(name);
        var me = await first.GetFromJsonAsync<JsonElement>("/api/admin/auth/me");
        Assert.True(me.TryGetProperty("mustChangePassword", out var mustChange) && mustChange.GetBoolean());
        Assert.False(me.GetProperty("twoFactorEnabled").GetBoolean());
        await Error(await first.GetAsync("/api/admin/licenses"), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
        await Error(await first.PostAsJsonAsync("/api/admin/accounts", new { userName = "blocked", password = fixture.Password, role = "ReadOnly" }), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
        await Error(await first.PostAsJsonAsync("/api/admin/security/two-factor/setup", new { password = fixture.Password }), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/security")).StatusCode);
        await Error(await first.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = "incorrect", newPassword = fixture.Password + "Changed1!" }), HttpStatusCode.BadRequest, "CURRENT_PASSWORD_INVALID");
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = fixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = "weak" })).StatusCode);
        var changed = await first.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = fixture.Password + "Changed1!" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        await ApiFixture.Csrf(first);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/admin/auth/me")).StatusCode);
        using var admin = await fixture.Login();
        var audit = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={id}");
        Assert.Contains("security.password", audit);
        Assert.DoesNotContain(fixture.Password, audit);
    }

    [Fact]
    public async Task TotpEnrollmentLoginRecoveryAndDisablePreserveSecurityBoundaries()
    {
        var (name, id) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        using var prior = await LoginPassword(name, ChangedPassword);
        var setup = await Setup(client);
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("authenticatorUri").GetString());
        var secret = setup.GetProperty("sharedKey").GetString()!;
        var status = await client.GetFromJsonAsync<JsonElement>("/api/admin/security");
        Assert.False(status.GetProperty("twoFactorEnabled").GetBoolean());
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/confirm", new { code = "bad" }), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        var confirmed = await client.PostAsJsonAsync("/api/admin/security/two-factor/confirm", new { code = Code(secret, fixture.Clock.GetUtcNow()) });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var codes = (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(code.Length >= 26));
        await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await prior.GetAsync("/api/admin/auth/me")).StatusCode);
        var state = await client.GetFromJsonAsync<JsonElement>("/api/admin/security");
        Assert.Equal(10, state.GetProperty("recoveryCodesRemaining").GetInt32());
        // Reading through another host proves the encrypted secret survives service/host recreation.
        await using var restarted = fixture.WithWebHostBuilder(_ => { });
        using var login = restarted.CreateClient();
        await ApiFixture.Csrf(login);
        var challenge = await Challenge(login, name);
        Assert.Equal(HttpStatusCode.Unauthorized, (await login.GetAsync("/api/admin/licenses")).StatusCode);
        await Error(await Finish(login, challenge, Code(secret, fixture.Clock.GetUtcNow())), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await Finish(login, challenge, Code(secret, fixture.Clock.GetUtcNow()))).StatusCode);
        await ApiFixture.Csrf(login);
        Assert.Equal(HttpStatusCode.OK, (await login.GetAsync("/api/admin/licenses")).StatusCode);
        using var recovery = fixture.CreateClient();
        await ApiFixture.Csrf(recovery);
        var recoveryChallenge = await Challenge(recovery, name);
        Assert.Equal(HttpStatusCode.OK, (await Finish(recovery, recoveryChallenge, codes[0], true)).StatusCode);
        await ApiFixture.Csrf(recovery);
        Assert.Equal(9, (await recovery.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        await Error(await recovery.PostAsJsonAsync("/api/admin/security/two-factor/disable", new { password = "wrong" }), HttpStatusCode.BadRequest, "CURRENT_PASSWORD_INVALID");
        Assert.Equal(HttpStatusCode.OK, (await recovery.PostAsJsonAsync("/api/admin/security/two-factor/disable", new { password = ChangedPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await login.GetAsync("/api/admin/auth/me")).StatusCode);
        Assert.False((await recovery.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("twoFactorEnabled").GetBoolean());
        using var admin = await fixture.Login();
        var audit = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={id}");
        Assert.DoesNotContain(secret, audit);
        Assert.All(codes, code => Assert.DoesNotContain(code, audit));
    }

    [Theory]
    [InlineData("enrollment")]
    [InlineData("totp")]
    [InlineData("recovery")]
    public async Task VerifiedSessionAllowsRepeatedWritesAndPasswordChangesAfterFiveMinutes(string method)
    {
        var (name, _) = await CreateAccount("Admin");
        using var enrolled = await ChangeInitialPassword(name);
        var (secret, codes) = await Enable(enrolled);
        using var client = method == "enrollment" ? enrolled : fixture.CreateClient();
        if (method != "enrollment")
        {
            await ApiFixture.Csrf(client);
            var challenge = await Challenge(client, name);
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.OK, (await Finish(client, challenge,
                method == "recovery" ? codes[0] : Code(secret, fixture.Clock.GetUtcNow()), method == "recovery")).StatusCode);
            await ApiFixture.Csrf(client);
        }
        var password = ChangedPassword;
        for (var operation = 0; operation < 2; operation++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(6));
            var appCode = "verified-session-" + Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/admin/applications", new CreateApplication(appCode, "Verified session"))).StatusCode);
            var nextPassword = password + "Again1!";
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = password, newPassword = nextPassword })).StatusCode);
            password = nextPassword;
            await ApiFixture.Csrf(client);
        }
        var status = await client.GetFromJsonAsync<JsonElement>("/api/admin/security");
        Assert.Equal(method == "recovery" ? 9 : 10, status.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.True(status.GetProperty("verificationExpiresAt").GetDateTimeOffset() > fixture.Clock.GetUtcNow());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/licenses")).StatusCode);
    }

    [Fact]
    public async Task RecoveryCodeCanBeConsumedOnlyOnceAcrossConcurrentRequests()
    {
        var (name, _) = await CreateAccount();
        using var first = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(first);
        using var second = fixture.CreateClient();
        await ApiFixture.Csrf(second);
        var challenge = await Challenge(second, name);
        Assert.Equal(HttpStatusCode.OK, (await Finish(second, challenge, codes[0], true)).StatusCode);
        await ApiFixture.Csrf(second);
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/admin/security/verify", new { code = codes[1], useRecoveryCode = true }),
            second.PostAsJsonAsync("/api/admin/security/verify", new { code = codes[1], useRecoveryCode = true }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(8, (await first.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Fact]
    public async Task ChallengesExpireAndPasswordResetInvalidatesChallengeWithoutRemovingMfa()
    {
        var (name, id) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(client);
        using var pending = fixture.CreateClient();
        await ApiFixture.Csrf(pending);
        var challenge = await Challenge(pending, name);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await Error(await Finish(pending, challenge, codes[0], true), HttpStatusCode.BadRequest, "TWO_FACTOR_CHALLENGE_INVALID");
        challenge = await Challenge(pending, name);
        using var admin = await fixture.Login();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/accounts/{id}/reset-password", new { password = fixture.Password })).StatusCode);
        await Error(await Finish(pending, challenge, codes[0], true), HttpStatusCode.BadRequest, "TWO_FACTOR_CHALLENGE_INVALID");
        var response = await pending.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password));
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresTwoFactor").GetBoolean());
    }

    [Fact]
    public async Task WrongSecondFactorLocksAccountAndCorrectPasswordDoesNotClearItsFailures()
    {
        var (name, _) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(client);
        using var pending = fixture.CreateClient();
        await ApiFixture.Csrf(pending);
        for (var i = 0; i < 5; i++)
        {
            var challenge = await Challenge(pending, name);
            await Error(await Finish(pending, challenge, "invalid"), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        }
        await Error(await pending.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, ChangedPassword)), HttpStatusCode.Unauthorized, "LOGIN_FAILED");
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        var unlocked = await Challenge(pending, name);
        Assert.Equal(HttpStatusCode.OK, (await Finish(pending, unlocked, codes[0], true)).StatusCode);
    }

    [Fact]
    public async Task PendingAndActiveSecretsAreEncryptedAndExpiredSetupCannotEnableMfa()
    {
        var (name, id) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var secret = (await Setup(client)).GetProperty("sharedKey").GetString()!;
        using (var scope = fixture.Services.CreateScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>().FindByIdAsync(id))!;
            Assert.NotNull(user.ProtectedPendingTotpSecret);
            Assert.True(user.ProtectedPendingTotpSecret != secret);
            Assert.Null(user.ProtectedTotpSecret);
            Assert.False(user.TwoFactorEnabled);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/confirm", new { code = Code(secret, fixture.Clock.GetUtcNow()) }), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        var (activeSecret, codes) = await Enable(client);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = (await db.Users.FindAsync(id))!;
            Assert.NotNull(user.ProtectedTotpSecret);
            Assert.True(user.ProtectedTotpSecret != activeSecret);
            Assert.Null(user.ProtectedPendingTotpSecret);
            var stored = await db.AdminRecoveryCodes.Where(x => x.UserId == id).ToListAsync();
            Assert.Equal(10, stored.Count);
            Assert.All(stored, row => Assert.True(row.Digest.Length == 64 && !codes.Contains(row.Digest)));
        }
    }

    [Fact]
    public async Task RecoveryRegenerationUsesCurrentPasswordInVerifiedSessionAndInvalidatesAllOldCodes()
    {
        var (name, _) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var (_, oldCodes) = await Enable(client);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/recovery-codes", new { password = "wrong" }), HttpStatusCode.BadRequest, "CURRENT_PASSWORD_INVALID");
        var response = await client.PostAsJsonAsync("/api/admin/security/two-factor/recovery-codes", new { password = ChangedPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCodes = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(10, newCodes.Length);
        Assert.Empty(oldCodes.Intersect(newCodes));
        Assert.Equal(10, (await client.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
        using var pending = fixture.CreateClient();
        await ApiFixture.Csrf(pending);
        var challenge = await Challenge(pending, name);
        await Error(await Finish(pending, challenge, oldCodes[0], true), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        await Error(await Finish(pending, challenge, oldCodes[^1], true), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        Assert.Equal(HttpStatusCode.OK, (await Finish(pending, challenge, newCodes[0], true)).StatusCode);
        await ApiFixture.Csrf(pending);
        Assert.Equal(9, (await pending.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("recovery-codes")]
    public async Task PasswordOnlySecurityActionsRequireMfaToBeEnabled(string action)
    {
        var (name, id) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        await Error(await client.PostAsJsonAsync($"/api/admin/security/two-factor/{action}", new { password = ChangedPassword }), HttpStatusCode.Conflict, "TWO_FACTOR_NOT_ENABLED");
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AdminRecoveryCodes.AnyAsync(x => x.UserId == id));
    }

    [Fact]
    public async Task TotpCodeAndLoginChallengeEachCanBeUsedOnlyOnce()
    {
        var (name, _) = await CreateAccount();
        using var first = await ChangeInitialPassword(name);
        var (secret, codes) = await Enable(first);
        using var second = fixture.CreateClient();
        await ApiFixture.Csrf(second);
        var challenge = await Challenge(second, name);
        Assert.Equal(HttpStatusCode.OK, (await Finish(second, challenge, codes[0], true)).StatusCode);
        await ApiFixture.Csrf(second);
        await Error(await Finish(second, challenge, codes[1], true), HttpStatusCode.BadRequest, "TWO_FACTOR_CHALLENGE_INVALID");
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var code = Code(secret, fixture.Clock.GetUtcNow());
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/admin/security/verify", new { code }),
            second.PostAsJsonAsync("/api/admin/security/verify", new { code }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("role")]
    public async Task AccountStateChangesInvalidatePendingChallenges(string action)
    {
        var (name, id) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(client);
        using var pending = fixture.CreateClient();
        await ApiFixture.Csrf(pending);
        var challenge = await Challenge(pending, name);
        using var admin = await fixture.Login();
        var changed = action == "disable"
            ? await admin.PostAsync($"/api/admin/accounts/{id}/disable", null)
            : await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "ReadOnly" });
        Assert.True(changed.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
        await Error(await Finish(pending, challenge, codes[0], true), HttpStatusCode.BadRequest, "TWO_FACTOR_CHALLENGE_INVALID");
    }

    [Fact]
    public async Task SecurityWritesRequireCsrfAndInvalidChallengeNeverAuthenticates()
    {
        var (name, _) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Error(await client.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = ChangedPassword, newPassword = ChangedPassword + "Again1!" }), HttpStatusCode.BadRequest, "CSRF_INVALID");
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/setup", new { password = ChangedPassword }), HttpStatusCode.BadRequest, "CSRF_INVALID");
        await ApiFixture.Csrf(client);
        await Enable(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/disable", new { password = ChangedPassword }), HttpStatusCode.BadRequest, "CSRF_INVALID");
        await Error(await client.PostAsJsonAsync("/api/admin/security/two-factor/recovery-codes", new { password = ChangedPassword }), HttpStatusCode.BadRequest, "CSRF_INVALID");
        using var anonymous = fixture.CreateClient();
        await ApiFixture.Csrf(anonymous);
        await Error(await Finish(anonymous, "malformed", "000000"), HttpStatusCode.BadRequest, "TWO_FACTOR_CHALLENGE_INVALID");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/security")).StatusCode);
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("session-expiry")]
    public async Task QueuedAccountWriteRechecksVerifiedSessionBeforeMutation(string invalidation)
    {
        var (name, id) = await CreateAccount("Admin");
        using var client = await ChangeInitialPassword(name);
        await Enable(client);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
        var createdName = "queued-security-" + Guid.NewGuid().ToString("N");
        var request = client.PostAsJsonAsync("/api/admin/accounts", new { userName = createdName, password = fixture.Password, role = "Admin" });
        var waiting = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (waiting == 0 && DateTime.UtcNow < deadline)
        {
            waiting = await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())").SingleAsync();
            if (waiting == 0) await Task.Delay(20);
        }
        if (invalidation == "session-expiry") fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        else
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByIdAsync(id))!;
            user.MustChangePassword = true;
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        }
        await tx.CommitAsync();
        Assert.Equal(1, waiting);
        Assert.Equal(HttpStatusCode.Unauthorized, (await request).StatusCode);
        Assert.False(await db.Users.AsNoTracking().AnyAsync(x => x.UserName == createdName));
    }

    [Fact]
    public async Task LoginAndSecondFactorShareIpAdmissionBeforeCodeConsumption()
    {
        var (name, _) = await CreateAccount();
        using var client = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(client);
        await using var limited = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:LoginPerMinute"] = "2" })));
        using var pending = limited.CreateClient();
        await ApiFixture.Csrf(pending);
        var challenge = await Challenge(pending, name);
        await Error(await Finish(pending, challenge, "invalid"), HttpStatusCode.BadRequest, "TWO_FACTOR_INVALID");
        var response = await Finish(pending, challenge, codes[0], true);
        await Error(response, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(10, (await client.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await pending.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task WrongPasswordsLockAccountUntilTheConfiguredBoundary()
    {
        var (name, _) = await CreateAccount();
        using var client = fixture.CreateClient();
        await ApiFixture.Csrf(client);
        for (var i = 0; i < 5; i++)
            await Error(await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, "invalid")), HttpStatusCode.Unauthorized, "LOGIN_FAILED");
        await Error(await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password)), HttpStatusCode.Unauthorized, "LOGIN_FAILED");
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password))).StatusCode);
    }

    [Fact]
    public async Task RequiredPasswordChangeUsesLoginVerificationAfterFiveMinutes()
    {
        var (name, id) = await CreateAccount();
        using var previous = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(previous);
        using var admin = await fixture.Login();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/accounts/{id}/reset-password", new { password = fixture.Password })).StatusCode);
        using var current = fixture.CreateClient();
        await ApiFixture.Csrf(current);
        var login = await current.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var challenge = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Finish(current, challenge, codes[0], true)).StatusCode);
        await ApiFixture.Csrf(current);
        Assert.True((await current.GetFromJsonAsync<JsonElement>("/api/admin/auth/me")).GetProperty("mustChangePassword").GetBoolean());
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var changed = await current.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = ChangedPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        await ApiFixture.Csrf(current);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.OK, (await current.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = ChangedPassword, newPassword = ChangedPassword + "Again1!" })).StatusCode);
        Assert.Equal(9, (await current.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("253402300799")]
    public async Task MissingOrInvalidSessionVerificationRequiresNewLogin(string? verification)
    {
        var (name, _) = await CreateAccount("Admin");
        using var enrolled = await ChangeInitialPassword(name);
        var (_, codes) = await Enable(enrolled);
        using var login = fixture.CreateClient();
        await ApiFixture.Csrf(login);
        var challenge = await Challenge(login, name);
        var completed = await Finish(login, challenge, codes[0], true);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        // Use a real authenticated ticket to model an incomplete or legacy session.
        // Only its MFA verification property is altered; identity and stamp stay valid.
        var cookie = SessionCookie(completed, properties =>
        {
            if (verification is null) properties.Items.Remove("two-factor-verified");
            else properties.Items["two-factor-verified"] = verification;
        });
        using var incomplete = fixture.CreateClient(new() { HandleCookies = false });
        incomplete.DefaultRequestHeaders.Add("Cookie", cookie);
        var read = await incomplete.GetAsync("/api/admin/licenses");
        await Error(read, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Contains(read.Headers.GetValues("Set-Cookie"), value => value.StartsWith("KeyValidation.Admin=;", StringComparison.Ordinal));
        await Error(await incomplete.PostAsJsonAsync("/api/admin/applications", new CreateApplication("unverified-" + Guid.NewGuid().ToString("N"), "Unverified")), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Error(await incomplete.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = ChangedPassword, newPassword = ChangedPassword + "Again1!" }), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Error(await incomplete.PostAsJsonAsync("/api/admin/security/two-factor/disable", new { password = ChangedPassword }), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Error(await incomplete.PostAsJsonAsync("/api/admin/security/two-factor/recovery-codes", new { password = ChangedPassword }), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await Error(await incomplete.PostAsJsonAsync("/api/admin/security/verify", new { code = codes[1], useRecoveryCode = true }), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal(9, (await login.GetFromJsonAsync<JsonElement>("/api/admin/security")).GetProperty("recoveryCodesRemaining").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoFactorLoginRemainsVerifiedWhenPrincipalCreationCrossesSecondBoundary(bool recovery)
    {
        var (name, _) = await CreateAccount();
        using var enrolled = await ChangeInitialPassword(name);
        var (secret, codes) = await Enable(enrolled);
        await using var delayed = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IUserClaimsPrincipalFactory<AdminUser>>(provider => new AdvancingPrincipalFactory(
                provider.GetRequiredService<UserManager<AdminUser>>(), provider.GetRequiredService<RoleManager<IdentityRole>>(),
                provider.GetRequiredService<IOptions<IdentityOptions>>(), fixture.Clock))));
        using var client = delayed.CreateClient();
        await ApiFixture.Csrf(client);
        var challenge = await Challenge(client, name);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await Finish(client, challenge, recovery ? codes[0] : Code(secret, fixture.Clock.GetUtcNow()), recovery)).StatusCode);
        await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    private sealed class AdvancingPrincipalFactory(UserManager<AdminUser> users, RoleManager<IdentityRole> roles,
        IOptions<IdentityOptions> options, ManualTime clock) : UserClaimsPrincipalFactory<AdminUser, IdentityRole>(users, roles, options)
    {
        public override async Task<ClaimsPrincipal> CreateAsync(AdminUser user)
        {
            var principal = await base.CreateAsync(user);
            clock.Advance(TimeSpan.FromSeconds(1));
            return principal;
        }
    }

    [Fact]
    public async Task VerifiedSessionStillExpiresAfterThirtyIdleMinutes()
    {
        var (name, _) = await CreateAccount("Admin");
        using var client = await ChangeInitialPassword(name);
        await Enable(client);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        await Error(await client.PostAsJsonAsync("/api/admin/applications", new CreateApplication("expired-" + Guid.NewGuid().ToString("N"), "Expired")), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task VerifiedSessionSurvivesSlidingRenewalButKeepsItsAbsoluteDeadline()
    {
        var (name, _) = await CreateAccount("Admin");
        using var client = await ChangeInitialPassword(name);
        await Enable(client);
        var sessionStarted = fixture.Clock.GetUtcNow();
        for (var interval = 0; interval < 23; interval++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = ChangedPassword, newPassword = ChangedPassword + "Again1!" })).StatusCode);
        await ApiFixture.Csrf(client);
        fixture.Clock.Advance(TimeSpan.FromMinutes(19));
        var status = await client.GetFromJsonAsync<JsonElement>("/api/admin/security");
        var deadline = status.GetProperty("verificationExpiresAt").GetDateTimeOffset();
        Assert.True(deadline > fixture.Clock.GetUtcNow());
        Assert.True(deadline <= sessionStarted.AddHours(8));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/admin/applications", new CreateApplication("sliding-" + Guid.NewGuid().ToString("N"), "Sliding session"))).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Error(await client.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = ChangedPassword + "Again1!", newPassword = ChangedPassword + "Again2!" }), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    private string SessionCookie(HttpResponseMessage response, Action<AuthenticationProperties> change)
    {
        var options = fixture.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var prefix = options.Cookie.Name + "=";
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(prefix, StringComparison.Ordinal));
        var ticket = options.TicketDataFormat.Unprotect(cookie.Split(';')[0][prefix.Length..]);
        Assert.NotNull(ticket);
        change(ticket.Properties);
        return prefix + options.TicketDataFormat.Protect(ticket);
    }

    private string ChangedPassword => fixture.Password + "Changed1!";
    private async Task<(string Name, string Id)> CreateAccount(string role = "Operator")
    {
        using var admin = await fixture.Login();
        var name = "security-" + Guid.NewGuid().ToString("N");
        var response = await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name, password = fixture.Password, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (name, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
    }
    private async Task<HttpClient> LoginPassword(string name, string password)
    {
        var client = fixture.CreateClient();
        await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, password))).StatusCode);
        await ApiFixture.Csrf(client);
        return client;
    }
    private async Task<HttpClient> ChangeInitialPassword(string name)
    {
        var client = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = ChangedPassword })).StatusCode);
        await ApiFixture.Csrf(client);
        return client;
    }
    private async Task<JsonElement> Setup(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/admin/security/two-factor/setup", new { password = ChangedPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<(string Secret, string[] Codes)> Enable(HttpClient client)
    {
        var setup = await Setup(client);
        var secret = setup.GetProperty("sharedKey").GetString()!;
        var response = await client.PostAsJsonAsync("/api/admin/security/two-factor/confirm", new { code = Code(secret, fixture.Clock.GetUtcNow()) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var codes = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        await ApiFixture.Csrf(client);
        return (secret, codes);
    }
    private async Task<string> Challenge(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, ChangedPassword));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("requiresTwoFactor").GetBoolean());
        return body.GetProperty("challengeToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Finish(HttpClient client, string token, string code, bool recovery = false) =>
        client.PostAsJsonAsync("/api/admin/auth/two-factor", new { challengeToken = token, code, useRecoveryCode = recovery });
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    // Independent RFC 4226 HOTP calculation; no production TOTP implementation is called.
    private static string Code(string key, DateTimeOffset now)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(key.Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes, counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
