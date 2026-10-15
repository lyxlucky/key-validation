using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class LocalClientFactAttribute : FactAttribute
{
    public LocalClientFactAttribute() { if (Environment.GetEnvironmentVariable("CLIENT_VALIDATION_MANIFEST") is null) Skip = "Requires freshly provisioned local API fixtures."; }
}

public sealed class ClientLiveTests
{
    [LocalClientFact]
    public Task BackendChangesDriveTheActualClientWindow() => ClientFormTests.OnUiThread(async () =>
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("CLIENT_VALIDATION_MANIFEST")!));
        var data = input.RootElement;
        var apiUrl = data.GetProperty("apiUrl").GetString()!;
        var appId = data.GetProperty("applicationId").GetString()!;
        var license = data.GetProperty("licenses")[0];
        var licenseId = license.GetProperty("id").GetString()!;
        var key = license.GetProperty("key").GetString()!;
        var profile = new DemoProfile { ApiUrl = apiUrl, AppCode = data.GetProperty("appCode").GetString()!, ClientVersion = "1.0.0", DeviceId = "client-validation-" + Guid.NewGuid().ToString("N"), DeviceLabel = "客户端联动验收" };
        using var admin = new HttpClient { BaseAddress = new(apiUrl), Timeout = TimeSpan.FromSeconds(15) };
        admin.DefaultRequestHeaders.Add("Cookie", Environment.GetEnvironmentVariable("CLIENT_ADMIN_COOKIE"));
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Environment.GetEnvironmentVariable("CLIENT_ADMIN_CSRF"));
        using var coordinator = new SessionCoordinator(p => DesktopTransport.Create(p, new SafeDiagnostics()), new MemoryTimeStore());
        using var form = new ClientForm(coordinator, profile, "客户端联动验收");
        form.Show();
        var card = (TextBox)Assert.Single(form.Controls.Find("loginKey", true));
        var login = (Button)Assert.Single(form.Controls.Find("login", true));
        var verify = (Button)Assert.Single(form.Controls.Find("verifyNow", true));
        var title = (Label)Assert.Single(form.Controls.Find("statusTitle", true));
        async Task Login()
        {
            card.Text = key; login.PerformClick();
            await Wait(() => coordinator.Snapshot().Authorized && title.Text == "授权正常", 15);
            Assert.Empty(card.Text);
        }
        async Task Write(string route, object body)
        {
            using var response = await admin.PostAsJsonAsync(route, body);
            Assert.True(response.IsSuccessStatusCode, "Admin operation failed: " + (int)response.StatusCode);
        }
        async Task VerifyRejected(string expected)
        {
            verify.PerformClick();
            await Wait(() => !coordinator.Snapshot().Authorized && title.Text == expected, 15);
            Assert.True(card.Visible);
            Assert.False(verify.Visible);
        }
        await Login();
        await Write($"/api/admin/licenses/{licenseId}/disable", new { });
        // Exercise the normal automatic heartbeat, with no manual verification.
        await Wait(() => title.Text == "卡密已禁用" && !coordinator.Snapshot().Authorized, 80);
        await Write($"/api/admin/licenses/{licenseId}/enable", new { });
        Assert.False(coordinator.Snapshot().Authorized);
        await Login();
        await Write($"/api/admin/licenses/{licenseId}/freeze", new { reason = "客户端真实冻结验收" });
        await VerifyRejected("卡密已冻结");
        await Write($"/api/admin/licenses/{licenseId}/unfreeze", new { reason = "客户端真实解冻验收" });
        await Login();
        var expiry = coordinator.Snapshot().ExpiresAt!.Value;
        await Write("/api/admin/license-operations", new { requestId = Guid.NewGuid(), licenseIds = new[] { licenseId }, action = "extend", additionalSeconds = 3600, reason = "客户端续期刷新验收" });
        verify.PerformClick();
        await Wait(() => coordinator.Snapshot().ExpiresAt > expiry, 15);
        Assert.Equal("授权正常", title.Text);
        using (var detailResponse = await admin.GetAsync($"/api/admin/licenses/{licenseId}"))
        {
            detailResponse.EnsureSuccessStatusCode();
            using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            var session = detail.RootElement.GetProperty("bindings").EnumerateArray().SelectMany(binding => binding.GetProperty("sessions").EnumerateArray()).First(session => session.GetProperty("revokedAt").ValueKind == JsonValueKind.Null);
            await Write($"/api/admin/sessions/{session.GetProperty("id").GetString()}/revoke", new { });
        }
        await VerifyRejected("登录已失效");
        await Login();
        await Write($"/api/admin/applications/{appId}/disable", new { });
        await VerifyRejected("应用已停用");
        await Write($"/api/admin/applications/{appId}/enable", new { });
        await Login();
        ((Button)Assert.Single(form.Controls.Find("logout", true))).PerformClick();
        await Wait(() => title.Text == "已退出登录" && !coordinator.Snapshot().IsBusy, 15);
        var expiryLicense = data.GetProperty("expiryLicense");
        key = expiryLicense.GetProperty("key").GetString()!;
        await Login();
        await Wait(() => title.Text == "卡密已到期" && !coordinator.Snapshot().Authorized, 10);
        await Write("/api/admin/license-operations", new { requestId = Guid.NewGuid(), licenseIds = new[] { expiryLicense.GetProperty("id").GetString()! }, action = "extend", additionalSeconds = 3600, reason = "到期后续期重新登录验收" });
        await Login();
        form.Close();
    });

    private static async Task Wait(Func<bool> predicate, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.True(predicate(), "The expected client state did not arrive.");
    }
}
