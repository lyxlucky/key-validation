namespace KeyValidation.Desktop;

public sealed class ClientForm : Form
{
    private static readonly Color Ink = Color.FromArgb(23, 32, 51);
    private static readonly Color Muted = Color.FromArgb(88, 101, 121);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private readonly SessionCoordinator coordinator;
    private readonly DemoProfile profile;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly Panel loginPage = new() { Name = "loginPage", Dock = DockStyle.Fill };
    private readonly Panel memberPage = new() { Name = "memberPage", Dock = DockStyle.Fill, Visible = false };
    private readonly TextBox card = new() { Name = "loginKey", UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "输入完整卡密", MaxLength = 1024, AccessibleName = "卡密" };
    private readonly Button login = Button("login", "登录", true);
    private readonly Button verify = Button("verifyNow", "立即验证");
    private readonly Button logout = Button("logout", "退出登录");
    private readonly Label statusTitle = Label("statusTitle", "请登录", 17, true);
    private readonly Label statusMessage = Label("statusMessage", "", 10);
    private readonly Panel banner = new() { Dock = DockStyle.Fill, Padding = new(20, 14, 20, 14) };
    private readonly Label expiry = Label("expiresAt", "—");
    private readonly Label verified = Label("lastVerifiedAt", "—");
    private readonly Label remaining = Label("leaseRemaining", "—");
    private readonly Label licenseId = Label("licenseId", "—");
    private readonly Label features = Label("features", "—");
    private readonly Label footnote = Label("connection", "", 9);
    private readonly ListBox history = new() { Name = "statusHistory", Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, AccessibleName = "状态记录", ForeColor = Muted };
    private bool operationBusy;
    private bool timerBusy;
    private bool closing;
    private bool closeReady;
    private bool resourcesDisposed;
    private string? renderedCode;
    private string? unexpectedError;
    private Guid? lastLicenseId;

    public ClientForm(SessionCoordinator coordinator, DemoProfile profile, string applicationName)
    {
        this.coordinator = coordinator;
        this.profile = profile;
        profile.Validate();
        if (profile.Mode != AuthorizationMode.Online) throw new ProfileException("OFFLINE_NETWORK_DISABLED");
        coordinator.Configure(profile);
        coordinator.SetAutomaticHeartbeat(true);
        Name = "KeyGateClient";
        Text = applicationName;
        Font = new Font("Microsoft YaHei UI", 10);
        ForeColor = Ink;
        BackColor = Color.FromArgb(245, 247, 251);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new(96, 96);
        ClientSize = new(760, 730);
        MinimumSize = new(600, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout(applicationName);
        card.Text = profile.LicenseKey ?? "";
        login.Click += async (_, _) => await RunAsync(() => coordinator.ActivateAsync(card.Text.Trim(), lifetime.Token));
        verify.Click += async (_, _) => await RunAsync(() => coordinator.HeartbeatAsync(lifetime.Token));
        logout.Click += async (_, _) => await RunAsync(() => coordinator.LogoutAsync(lifetime.Token));
        timer.Tick += async (_, _) =>
        {
            if (timerBusy || closing || IsDisposed) return;
            timerBusy = true;
            try { await coordinator.TickAsync(); if (!IsDisposed) Render(); }
            catch { ReportError(); }
            finally { timerBusy = false; }
        };
        Shown += (_, _) => { Render(); card.Focus(); timer.Start(); };
        FormClosing += OnClosing;
        Render();
    }

    private void BuildLayout(string applicationName)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28, 24, 28, 16), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 66));
        root.RowStyles.Add(new(SizeType.Absolute, 126));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 42));
        Controls.Add(root);
        var heading = Label("appHeading", applicationName, 21, true);
        heading.Dock = DockStyle.Fill;
        root.Controls.Add(heading, 0, 0);
        var bannerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bannerLayout.RowStyles.Add(new(SizeType.Absolute, 34));
        bannerLayout.RowStyles.Add(new(SizeType.Percent, 100));
        bannerLayout.Controls.Add(statusTitle, 0, 0);
        bannerLayout.Controls.Add(statusMessage, 0, 1);
        banner.Controls.Add(bannerLayout);
        banner.Margin = new(0, 0, 0, 18);
        root.Controls.Add(banner, 0, 1);
        var cardPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(24), Margin = new(0), AutoScroll = true };
        cardPanel.Controls.Add(loginPage);
        cardPanel.Controls.Add(memberPage);
        root.Controls.Add(cardPanel, 0, 2);
        footnote.Dock = DockStyle.Fill;
        footnote.TextAlign = ContentAlignment.MiddleLeft;
        footnote.Text = $"自动验证已开启   ·   客户端 {profile.ClientVersion}";
        root.Controls.Add(footnote, 0, 3);

        var loginLayout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 355, ColumnCount = 1, RowCount = 7 };
        foreach (var height in new[] { 44, 54, 28, 42, 38, 56, 70 }) loginLayout.RowStyles.Add(new(SizeType.Absolute, height));
        loginLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        loginPage.Controls.Add(loginLayout);
        loginLayout.Controls.Add(Label("loginHeading", "使用卡密登录", 19, true), 0, 0);
        loginLayout.Controls.Add(Label("loginDescription", "登录后将自动检查授权。后台修改卡密状态后，应用会显示对应提示并结束失效登录。", 10), 0, 1);
        loginLayout.Controls.Add(Label("keyLabel", "卡密", 10, true), 0, 2);
        card.Margin = new(0, 3, 0, 3);
        loginLayout.Controls.Add(card, 0, 3);
        var show = new CheckBox { Name = "showKey", Text = "显示卡密", AutoSize = true, Margin = new(0, 6, 0, 0), AccessibleName = "显示卡密" };
        show.CheckedChanged += (_, _) => card.UseSystemPasswordChar = !show.Checked;
        loginLayout.Controls.Add(show, 0, 4);
        login.Dock = DockStyle.Fill;
        login.Margin = new(0, 6, 0, 6);
        loginLayout.Controls.Add(login, 0, 5);
        loginLayout.Controls.Add(Label("loginHelp", "卡密属于当前应用。恢复、解冻或续期后，请重新输入卡密登录。", 9), 0, 6);

        var memberLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        memberLayout.ColumnStyles.Add(new(SizeType.Percent, 100));
        memberLayout.RowStyles.Add(new(SizeType.Absolute, 40));
        memberLayout.RowStyles.Add(new(SizeType.Absolute, 220));
        memberLayout.RowStyles.Add(new(SizeType.Absolute, 52));
        memberLayout.RowStyles.Add(new(SizeType.Absolute, 30));
        memberLayout.RowStyles.Add(new(SizeType.Percent, 100));
        memberPage.Controls.Add(memberLayout);
        memberLayout.Controls.Add(Label("memberHeading", "当前授权", 17, true), 0, 0);
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new(0, 2, 0, 10) };
        details.ColumnStyles.Add(new(SizeType.Absolute, 110));
        details.ColumnStyles.Add(new(SizeType.Percent, 100));
        AddDetail(details, 0, "到期时间", expiry);
        AddDetail(details, 1, "最近验证", verified);
        AddDetail(details, 2, "验证有效期", remaining);
        AddDetail(details, 3, "授权标识", licenseId);
        AddDetail(details, 4, "当前设备", Label("deviceLabel", profile.DeviceLabel));
        AddDetail(details, 5, "授权功能", features);
        memberLayout.Controls.Add(details, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new(0, 3, 0, 0) };
        var copyId = Button("copyLicenseId", "复制授权标识");
        copyId.Click += (_, _) =>
        {
            if (lastLicenseId is null) return;
            try { Clipboard.SetText(lastLicenseId.Value.ToString()); copyId.Text = "已复制"; }
            catch { copyId.Text = "复制失败"; }
        };
        actions.Controls.AddRange([verify, copyId, logout]);
        memberLayout.Controls.Add(actions, 0, 2);
        memberLayout.Controls.Add(Label("historyHeading", "状态记录", 10, true), 0, 3);
        memberLayout.Controls.Add(history, 0, 4);
    }

    private static void AddDetail(TableLayoutPanel panel, int row, string caption, Label value)
    {
        panel.RowStyles.Add(new(SizeType.Percent, 100f / 6));
        panel.Controls.Add(Label("", caption, 9), 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private async Task RunAsync(Func<Task<OperationResult>> operation)
    {
        if (operationBusy || closing || coordinator.Snapshot().IsBusy) return;
        operationBusy = true;
        unexpectedError = null;
        Render();
        try { await operation(); }
        catch { ReportError(); }
        finally { operationBusy = false; if (!IsDisposed) Render(); }
    }

    private void Render()
    {
        if (IsDisposed) return;
        var snapshot = coordinator.Snapshot();
        var state = ClientStatus.From(snapshot);
        if (unexpectedError is not null) state = new("UNEXPECTED_ERROR", "操作未完成", unexpectedError, false, true);
        var busy = operationBusy || snapshot.IsBusy || closing;
        login.Enabled = !busy;
        card.Enabled = !busy;
        verify.Enabled = !busy && state.IsLoggedIn;
        logout.Enabled = !busy && state.IsLoggedIn;
        login.Text = busy && !state.IsLoggedIn ? "正在验证…" : "登录";
        if (state.IsLoggedIn && !memberPage.Visible)
        {
            card.Clear();
            AcceptButton = null;
        }
        else if (!state.IsLoggedIn) AcceptButton = login;
        memberPage.Visible = state.IsLoggedIn;
        loginPage.Visible = !state.IsLoggedIn;
        statusTitle.Text = state.Title;
        statusMessage.Text = state.Message;
        banner.BackColor = state.IsWarning ? Color.FromArgb(255, 247, 225) : state.IsLoggedIn ? Color.FromArgb(235, 248, 240) :
            state.Code is "AUTHORIZATION_REQUIRED" or "CONFIGURATION_LOADED" or "LOGOUT_CONFIRMED" ? Color.FromArgb(237, 244, 255) : Color.FromArgb(255, 240, 240);
        statusTitle.ForeColor = state.IsWarning ? Color.FromArgb(146, 95, 18) : state.IsLoggedIn ? Color.FromArgb(21, 128, 61) : Ink;
        if (snapshot.LicenseId is { } id) lastLicenseId = id;
        expiry.Text = DateText(snapshot.ExpiresAt);
        verified.Text = DateText(snapshot.LastVerifiedAt);
        remaining.Text = state.IsLoggedIn ? $"{Math.Max(0, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds))} 秒" : "—";
        licenseId.Text = snapshot.LicenseId?.ToString() ?? "—";
        features.Text = snapshot.Features.Length == 0 ? "基础授权" : string.Join("、", snapshot.Features);
        if (renderedCode != state.Code)
        {
            history.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}   {state.Title}");
            while (history.Items.Count > 8) history.Items.RemoveAt(history.Items.Count - 1);
            renderedCode = state.Code;
        }
    }

    public void ReportError()
    {
        if (IsDisposed || closing) return;
        coordinator.Clear();
        unexpectedError = "验证过程中出现异常，当前登录已结束。请重新登录。";
        Render();
    }

    private async void OnClosing(object? sender, FormClosingEventArgs args)
    {
        if (closeReady) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        timer.Stop();
        card.Clear();
        Render();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await coordinator.LogoutAsync(timeout.Token); }
        catch { coordinator.Clear(); }
        closeReady = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            timer.Dispose(); lifetime.Cancel(); lifetime.Dispose(); coordinator.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string DateText(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    private static Label Label(string name, string text, float size = 10, bool bold = false) => new()
    {
        Name = name, Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Font = new("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = bold ? Ink : Muted, Margin = new(0), AccessibleName = name
    };
    private static Button Button(string name, string text, bool primary = false) => new()
    {
        Name = name, Text = text, Height = 40, Width = 132, FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink,
        Margin = new(0, 0, 12, 0), Cursor = Cursors.Hand, AccessibleName = text,
        FlatAppearance = { BorderColor = primary ? Blue : Color.FromArgb(216, 224, 235) }
    };
}
