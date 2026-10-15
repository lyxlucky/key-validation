using System.Net;
using System.Net.Http.Json;
using System.Windows.Forms;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class ClientFormTests
{
    [Fact]
    public Task AStalledAutomaticHeartbeatCannotKeepTheExpiredMemberPageVisible() => OnUiThread(async () =>
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        using var form = new ClientForm(fixture.Coordinator, fixture.Profile, "授权客户端");
        form.Show();
        Find<TextBox>(form, "loginKey").Text = "synthetic-private-card";
        Find<Button>(form, "login").PerformClick();
        await Until(() => Find<Panel>(form, "memberPage").Visible);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = (_, token) => response.Task.WaitAsync(token);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await Until(() => fixture.Coordinator.Snapshot().IsBusy);
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));
        try
        {
            await Until(() => Find<Panel>(form, "loginPage").Visible);
            Assert.False(Find<Panel>(form, "memberPage").Visible);
            Assert.Equal("验证已超时", Find<Label>(form, "statusTitle").Text);
        }
        finally { response.TrySetResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = "SESSION_EXPIRED" }) }); }
        await Until(() => !fixture.Coordinator.Snapshot().IsBusy);
        form.Close();
    });
    [Fact]
    public Task SuccessfulLoginClearsTheKeyAndHeartbeatRejectionReturnsToLogin() => OnUiThread(async () =>
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        using var form = new ClientForm(fixture.Coordinator, fixture.Profile, "授权客户端");
        form.Show();
        var card = Find<TextBox>(form, "loginKey");
        card.Text = "synthetic-private-card";
        Find<Button>(form, "login").PerformClick();
        await Until(() => Find<Panel>(form, "memberPage").Visible);
        Assert.Empty(card.Text);
        Assert.False(Find<Panel>(form, "loginPage").Visible);
        var screenshotDirectory = Environment.GetEnvironmentVariable("CLIENT_UI_SCREENSHOTS");
        if (!string.IsNullOrEmpty(screenshotDirectory)) Save(form, screenshotDirectory, "authorized.png");
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = "LICENSE_FROZEN" }) });
        Find<Button>(form, "verifyNow").PerformClick();
        await Until(() => Find<Panel>(form, "loginPage").Visible);
        Assert.False(Find<Panel>(form, "memberPage").Visible);
        Assert.Equal("卡密已冻结", Find<Label>(form, "statusTitle").Text);
        if (!string.IsNullOrEmpty(screenshotDirectory)) Save(form, screenshotDirectory, "frozen.png");
        form.Close();
    });

    [Fact]
    public Task LoginPendingLocksSubmissionAndDoesNotOpenTheMemberPage() => OnUiThread(async () =>
    {
        using var fixture = new CoordinatorFixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = (_, token) => response.Task.WaitAsync(token);
        using var form = new ClientForm(fixture.Coordinator, fixture.Profile, "授权客户端");
        form.Show();
        Find<TextBox>(form, "loginKey").Text = "synthetic-private-card";
        var button = Find<Button>(form, "login");
        button.PerformClick();
        Assert.False(button.Enabled);
        Assert.False(Find<TextBox>(form, "loginKey").Enabled);
        Assert.False(Find<Panel>(form, "memberPage").Visible);
        response.SetResult(fixture.Grant());
        await Until(() => Find<Panel>(form, "memberPage").Visible);
        fixture.Coordinator.Clear();
        form.Close();
    });

    private static T Find<T>(Control form, string name) where T : Control => Assert.IsType<T>(Assert.Single(form.Controls.Find(name, true)));
    private static async Task Until(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 150 && !predicate(); attempt++) await Task.Delay(20);
        Assert.True(predicate());
    }
    private static void Save(Form form, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height });
        bitmap.Save(Path.Combine(directory, name));
    }
    internal static Task OnUiThread(Func<Task> test)
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var host = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
            host.Shown += async (_, _) =>
            {
                try { await test(); result.SetResult(); }
                catch (Exception error) { result.SetException(error); }
                finally { host.Close(); }
            };
            Application.Run(host);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return result.Task;
    }
}
