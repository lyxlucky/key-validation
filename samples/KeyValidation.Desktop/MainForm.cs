using System.Diagnostics;
using System.Text;

namespace KeyValidation.Desktop;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(23, 32, 51);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private readonly SessionCoordinator coordinator;
    private readonly SafeDiagnostics diagnostics;
    private readonly IFilePickers pickers;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 250 };
    private readonly TextBox apiUrl = Input("apiUrl", "API 地址");
    private readonly TextBox appCode = Input("appCode", "应用标识");
    private readonly TextBox clientVersion = Input("clientVersion", "客户端版本，可留空");
    private readonly TextBox deviceId = Input("deviceId", "设备标识，默认遮蔽");
    private readonly TextBox deviceLabel = Input("deviceLabel", "设备名称");
    private readonly TextBox card = Input("licenseKey", "卡密，默认遮蔽");
    private readonly CheckBox showSecrets = new() { Name = "showSecrets", Text = "显示敏感输入", AutoSize = true, AccessibleName = "显示卡密和设备标识", Margin = new(8, 8, 8, 0) };
    private readonly ComboBox mode = new() { Name = "authorizationMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, AccessibleName = "授权模式" };
    private readonly CheckBox automatic = new() { Name = "automaticHeartbeat", Text = "自动心跳", Checked = true, AutoSize = true, AccessibleName = "自动心跳开关", Margin = new(8, 8, 16, 0) };
    private readonly Button loadProfile = ActionButton("loadProfile", "载入测试配置", 132);
    private readonly Button clear = ActionButton("clearAuthorization", "清除本地授权", 132);
    private readonly Button activate = ActionButton("activate", "卡密激活", 110, primary: true);
    private readonly Button trial = ActionButton("activateTrial", "领取试用", 106);
    private readonly Button heartbeat = ActionButton("heartbeat", "立即心跳", 106);
    private readonly Button logout = ActionButton("logout", "退出会话", 106);
    private readonly Button release = ActionButton("releaseDevice", "释放当前设备", 136);
    private readonly Button versionCheck = ActionButton("checkVersion", "检查公开版本", 136);
    private readonly Button chooseLicense = ActionButton("chooseOfflineLicense", "选择离线许可证", 156);
    private readonly Button chooseKeys = ActionButton("chooseTrustedKeys", "选择独立可信公钥", 156);
    private readonly Button verifyOffline = ActionButton("verifyOffline", "验证离线授权", 136, primary: true);
    private readonly Button basic = ActionButton("basicAction", "执行基础操作", 148, primary: true);
    private readonly Button export = ActionButton("exportCsv", "导出演示 CSV", 148);
    private readonly Label licenseName = TextLabel("offlineLicenseName", "未选择 .kglicense 文件", 620);
    private readonly Label keysName = TextLabel("trustedKeysName", "未选择公钥 JSON", 400);
    private readonly Label policyNotice = TextLabel("modeNotice", "", 620);
    private readonly Label protectedReason = TextLabel("protectedReason", "尚未授权", 670);
    private readonly Label operationMessage = TextLabel("operationMessage", "", 800);
    private readonly Label progress = TextLabel("operationProgress", "", 650);
    private readonly RichTextBox status = new() { Name = "authorizationStatus", ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White, ForeColor = Ink, AccessibleName = "当前授权和版本状态", DetectUrls = false, TabStop = true };
    private readonly ListView logList = new() { Name = "diagnostics", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false, AccessibleName = "安全诊断记录", MultiSelect = false };
    private readonly TabControl tabs = new() { Name = "mainTabs", Dock = DockStyle.Fill, AccessibleName = "授权和诊断页面" };
    private readonly FlowLayoutPanel onlinePanel = VerticalFlow("onlineActions");
    private readonly FlowLayoutPanel offlinePanel = VerticalFlow("offlineActions");
    private bool suppressChanges;
    private bool configurationDirty;
    private int uiGeneration;
    private int pendingOperations;
    private bool timerBusy;
    private bool resourcesDisposed;
    private long renderedLogRevision = -1;
    private string? offlineLicensePath;
    private string? trustedKeysPath;
    private string? messageOverride;
    internal bool OperationPending => pendingOperations > 0;

    public MainForm(SessionCoordinator coordinator, SafeDiagnostics diagnostics, DemoProfile initialProfile, IFilePickers? pickers = null)
    {
        this.coordinator = coordinator;
        this.diagnostics = diagnostics;
        this.pickers = pickers ?? new NativeFilePickers();
        Name = "KeyValidationDesktop";
        Text = "KeyGate · Windows 授权测试台";
        Font = new Font("Microsoft YaHei UI", 9.5F);
        ForeColor = Ink;
        BackColor = Color.FromArgb(245, 247, 251);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1120, 840);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        deviceId.UseSystemPasswordChar = true;
        card.UseSystemPasswordChar = true;
        mode.Items.AddRange(["在线授权", "离线验证"]);
        BuildLayout();
        WireEvents();
        ApplyProfile(initialProfile);
        refreshTimer.Tick += async (_, _) =>
        {
            if (timerBusy || IsDisposed) return;
            timerBusy = true;
            try
            {
                var result = await coordinator.TickAsync();
                if (result.Code is not ("IDLE" or "REQUEST_IN_PROGRESS")) diagnostics.Record(DiagnosticOperation.Heartbeat, code: result.Code);
                if (!IsDisposed) Render();
            }
            catch { if (!IsDisposed) ReportError("UNEXPECTED_ERROR"); }
            finally { timerBusy = false; }
        };
        refreshTimer.Start();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new(14) };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 48));
        root.RowStyles.Add(new(SizeType.Absolute, 152));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new(SizeType.AutoSize));
        heading.ColumnStyles.Add(new(SizeType.AutoSize));
        heading.Controls.Add(new Label { Text = "KeyGate  /  授权测试台", Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        heading.Controls.Add(loadProfile, 1, 0);
        heading.Controls.Add(clear, 2, 0);
        root.Controls.Add(heading, 0, 0);

        var configuration = Group("连接与设备 · 授权期间锁定，清除后可修改");
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new(4, 0, 4, 0) };
        for (var i = 0; i < 3; i++) fields.ColumnStyles.Add(new(SizeType.Percent, 100F / 3));
        fields.RowStyles.Add(new(SizeType.Percent, 50)); fields.RowStyles.Add(new(SizeType.Percent, 50));
        fields.Controls.Add(Field("API 地址", apiUrl, 0), 0, 0);
        fields.Controls.Add(Field("应用标识", appCode, 1), 1, 0);
        fields.Controls.Add(Field("客户端版本（可留空）", clientVersion, 2), 2, 0);
        fields.Controls.Add(Field("设备标识（默认遮蔽）", deviceId, 3), 0, 1);
        fields.Controls.Add(Field("设备名称", deviceLabel, 4), 1, 1);
        fields.Controls.Add(new Label { Text = "配置只在内存中使用；默认设备标识会稳定保存。\n清除或切换模式会取消旧请求。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(88, 101, 121), Padding = new(8, 4, 0, 0), AutoEllipsis = true }, 2, 1);
        configuration.Controls.Add(fields);
        root.Controls.Add(configuration, 0, 1);
        root.Controls.Add(tabs, 0, 2);

        var authorization = new TabPage("授权与演示") { BackColor = BackColor, AutoScroll = true, Padding = new(6) };
        var diagnosticPage = new TabPage("诊断与帮助") { BackColor = BackColor, Padding = new(8) };
        tabs.TabPages.AddRange([authorization, diagnosticPage]);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, MinimumSize = new(0, 450), ColumnCount = 1, RowCount = 6, BackColor = BackColor };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.AutoSize));
        content.RowStyles.Add(new(SizeType.AutoSize));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 40));
        content.RowStyles.Add(new(SizeType.AutoSize));
        content.RowStyles.Add(new(SizeType.Absolute, 24));
        authorization.Controls.Add(content);
        var modeRow = Row();
        modeRow.Controls.Add(new Label { Text = "授权模式", AutoSize = true, Margin = new(4, 7, 8, 0) });
        modeRow.Controls.Add(mode); modeRow.Controls.Add(policyNotice);
        content.Controls.Add(modeRow, 0, 0);
        var actions = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        onlinePanel.Dock = DockStyle.Top; offlinePanel.Dock = DockStyle.Top;
        actions.Controls.Add(onlinePanel); actions.Controls.Add(offlinePanel);
        content.Controls.Add(actions, 0, 1);
        card.Width = 270; card.Dock = DockStyle.None; card.Margin = new(4, 4, 8, 0);
        var activationRow = Row();
        activationRow.Controls.Add(new Label { Text = "卡密", AutoSize = true, Margin = new(4, 8, 4, 0) });
        activationRow.Controls.Add(card); activationRow.Controls.Add(showSecrets); activationRow.Controls.Add(activate); activationRow.Controls.Add(trial);
        var sessionRow = Row();
        sessionRow.Controls.AddRange([heartbeat, automatic, logout, release, versionCheck]);
        onlinePanel.Controls.Add(activationRow); onlinePanel.Controls.Add(sessionRow);
        var fileRow = Row(); fileRow.Controls.AddRange([chooseLicense, licenseName]);
        var keyRow = Row(); keyRow.Controls.AddRange([chooseKeys, keysName, verifyOffline]);
        offlinePanel.Controls.Add(fileRow); offlinePanel.Controls.Add(keyRow);
        var current = Group("当前授权 · 状态与按钮仅作提示，操作时重新校验");
        current.Padding = new(12, 6, 12, 8); current.MinimumSize = new(0, 140); current.Controls.Add(status);
        content.Controls.Add(current, 0, 2);
        operationMessage.Dock = DockStyle.Fill; operationMessage.AutoSize = false; operationMessage.Padding = new(8, 4, 4, 0);
        content.Controls.Add(operationMessage, 0, 3);
        var protectedGroup = Group("受保护操作"); protectedGroup.AutoSize = true;
        var protectedLayout = VerticalFlow("protectedActions"); protectedLayout.Dock = DockStyle.Fill;
        var protectedButtons = Row(); protectedButtons.Controls.AddRange([basic, export]);
        protectedReason.Margin = new(8, 2, 4, 4); protectedReason.AutoSize = false;
        protectedReason.Width = 490; protectedReason.Height = Font.Height * 2 + 4;
        protectedButtons.Controls.Add(protectedReason);
        protectedLayout.Controls.Add(protectedButtons);
        protectedGroup.Controls.Add(protectedLayout); content.Controls.Add(protectedGroup, 0, 4);
        progress.Dock = DockStyle.Fill; progress.Font = new Font(Font.FontFamily, 8.5F);
        progress.Margin = new(8, 0, 4, 0); progress.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(progress, 0, 5);

        var diagnosticLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        diagnosticLayout.RowStyles.Add(new(SizeType.Absolute, 44));
        diagnosticLayout.RowStyles.Add(new(SizeType.Percent, 100));
        diagnosticLayout.RowStyles.Add(new(SizeType.Absolute, 44));
        diagnosticLayout.RowStyles.Add(new(SizeType.Absolute, 76));
        diagnosticPage.Controls.Add(diagnosticLayout);
        diagnosticLayout.Controls.Add(new Label { Text = "最多保留 200 条安全元数据；不记录卡密、令牌、设备原文、正文或认证头。", Dock = DockStyle.Fill, Padding = new(4, 8, 0, 0) }, 0, 0);
        logList.Columns.Add("时间", 92); logList.Columns.Add("操作", 90); logList.Columns.Add("方法 / 固定路径", 360);
        logList.Columns.Add("HTTP", 64); logList.Columns.Add("耗时 ms", 80); logList.Columns.Add("安全代码", 200);
        diagnosticLayout.Controls.Add(logList, 0, 1);
        var logButtons = Row();
        var clearLog = ActionButton("clearDiagnostics", "清除诊断", 120);
        var exportLog = ActionButton("exportDiagnostics", "导出安全诊断", 144);
        clearLog.Click += (_, _) => { diagnostics.Clear(); Render(); };
        exportLog.Click += (_, _) => SaveDiagnostics();
        logButtons.Controls.AddRange([clearLog, exportLog]); diagnosticLayout.Controls.Add(logButtons, 0, 2);
        var help = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        help.Controls.Add(new Label { Text = "后台：应用管理（卡种 / 试用）、卡密管理（设备 / 会话 / 冻结）、客户端版本、离线授权。", AutoSize = true, Margin = new(4, 4, 4, 8) });
        var links = Row(); links.Controls.Add(HelpLink("从零配置与使用", Path.Combine("help", "desktop-quickstart.md")));
        links.Controls.Add(HelpLink("本地手动用例与配置说明", "README.md")); links.Controls.Add(HelpLink("完整 API 接入协议", Path.Combine("help", "api-contract.md")));
        help.Controls.Add(links); diagnosticLayout.Controls.Add(help, 0, 3);
    }

    private void WireEvents()
    {
        foreach (var input in new[] { apiUrl, appCode, clientVersion, deviceId, deviceLabel }) input.TextChanged += (_, _) => ConfigurationChanged();
        mode.SelectedIndexChanged += (_, _) => ConfigurationChanged();
        showSecrets.CheckedChanged += (_, _) => { card.UseSystemPasswordChar = !showSecrets.Checked; deviceId.UseSystemPasswordChar = !showSecrets.Checked; };
        automatic.CheckedChanged += (_, _) => coordinator.SetAutomaticHeartbeat(automatic.Checked);
        loadProfile.Click += async (_, _) =>
        {
            var path = SelectFile(FilePurpose.Profile);
            if (path is not null) await LoadProfileAsync(path);
        };
        clear.Click += (_, _) => { uiGeneration++; coordinator.Clear(); messageOverride = null; Render(); };
        activate.Click += async (_, _) => await RunAsync(DiagnosticOperation.Activate, () => coordinator.ActivateAsync(card.Text.Trim(), lifetime.Token), applyConfiguration: true);
        trial.Click += async (_, _) => await RunAsync(DiagnosticOperation.Trial, () => coordinator.ActivateTrialAsync(lifetime.Token), applyConfiguration: true);
        heartbeat.Click += async (_, _) => await RunAsync(DiagnosticOperation.Heartbeat, () => coordinator.HeartbeatAsync(lifetime.Token));
        logout.Click += async (_, _) => await RunAsync(DiagnosticOperation.Logout, () => coordinator.LogoutAsync(lifetime.Token));
        release.Click += async (_, _) => await RunAsync(DiagnosticOperation.ReleaseDevice, () => coordinator.ReleaseDeviceAsync(lifetime.Token));
        versionCheck.Click += async (_, _) => await RunAsync(DiagnosticOperation.Version, () => coordinator.CheckVersionAsync(lifetime.Token), applyConfiguration: true);
        chooseLicense.Click += (_, _) => SelectOfflineFile(FilePurpose.OfflineLicense);
        chooseKeys.Click += (_, _) => SelectOfflineFile(FilePurpose.TrustedKeys);
        verifyOffline.Click += async (_, _) => await RunAsync(DiagnosticOperation.OfflineVerify,
            () => coordinator.ValidateOfflineAsync(offlineLicensePath ?? "", trustedKeysPath ?? "", lifetime.Token), applyConfiguration: true);
        basic.Click += (_, _) => { messageOverride = null; var result = coordinator.PerformBasicAction(); diagnostics.Record(DiagnosticOperation.BasicAction, code: result.Code); Render(); };
        export.Click += (_, _) =>
        {
            var path = SelectFile(FilePurpose.ProtectedCsv);
            if (path is null) return;
            messageOverride = null;
            var result = coordinator.ExportCsv(path); // Rechecks after the modal file chooser, immediately before writing.
            diagnostics.Record(DiagnosticOperation.ExportCsv, code: result.Code);
            Render();
        };
    }

    internal async Task LoadProfileAsync(string path)
    {
        uiGeneration++;
        var generation = uiGeneration;
        coordinator.Clear();
        await RunAsync(DiagnosticOperation.ProfileLoad, async () =>
        {
            var profile = await DemoProfile.LoadAsync(path, lifetime.Token);
            if (generation != uiGeneration || IsDisposed) return new(false, "REQUEST_SUPERSEDED");
            ApplyProfile(profile);
            return new(true, "CONFIGURATION_LOADED");
        });
    }

    private void ApplyProfile(DemoProfile profile)
    {
        profile.Validate();
        suppressChanges = true;
        try
        {
            apiUrl.Text = profile.ApiUrl; appCode.Text = profile.AppCode; clientVersion.Text = profile.ClientVersion;
            deviceId.Text = profile.DeviceId; deviceLabel.Text = profile.DeviceLabel; card.Text = profile.LicenseKey ?? "";
            mode.SelectedIndex = profile.Mode == AuthorizationMode.Online ? 0 : 1;
            offlineLicensePath = profile.OfflineLicensePath; trustedKeysPath = profile.TrustedKeysPath;
            showSecrets.Checked = false; card.UseSystemPasswordChar = true; deviceId.UseSystemPasswordChar = true;
            coordinator.Configure(profile); configurationDirty = false; messageOverride = null;
        }
        finally { suppressChanges = false; }
        Render();
    }

    private void ConfigurationChanged()
    {
        if (suppressChanges) return;
        uiGeneration++; coordinator.Clear(); configurationDirty = true; messageOverride = null;
        Render();
    }

    private void EnsureConfiguration()
    {
        if (!configurationDirty) return;
        coordinator.Configure(new DemoProfile
        {
            Mode = mode.SelectedIndex == 1 ? AuthorizationMode.Offline : AuthorizationMode.Online,
            ApiUrl = apiUrl.Text.Trim(), AppCode = appCode.Text.Trim().ToLowerInvariant(), ClientVersion = clientVersion.Text,
            DeviceId = deviceId.Text, DeviceLabel = deviceLabel.Text, OfflineLicensePath = offlineLicensePath, TrustedKeysPath = trustedKeysPath
        });
        configurationDirty = false;
    }

    private async Task RunAsync(DiagnosticOperation operation, Func<Task<OperationResult>> action, bool applyConfiguration = false)
    {
        pendingOperations++;
        messageOverride = null;
        try
        {
            if (applyConfiguration) EnsureConfiguration();
            var task = action();
            Render();
            var result = await task;
            diagnostics.Record(operation, code: result.Code);
        }
        catch (Exception error)
        {
            if (!IsDisposed) ReportError(error is ProfileException profileError ? profileError.Code :
                error is OperationCanceledException ? "REQUEST_CANCELLED" : "UNEXPECTED_ERROR");
        }
        finally
        {
            pendingOperations--;
            if (!IsDisposed) Render();
        }
    }

    private void SelectOfflineFile(FilePurpose purpose)
    {
        var path = SelectFile(purpose);
        if (path is null) return;
        uiGeneration++; coordinator.Clear(); messageOverride = null;
        if (purpose == FilePurpose.OfflineLicense) offlineLicensePath = path; else trustedKeysPath = path;
        Render();
    }

    private void SaveDiagnostics()
    {
        var path = SelectFile(FilePurpose.Diagnostics);
        if (path is null) return;
        try { diagnostics.ExportCsv(path); messageOverride = "安全诊断已导出。"; Render(); }
        catch { ReportError("FILE_WRITE_FAILED"); }
    }

    internal void ReportError(string code)
    {
        messageOverride = ErrorCatalog.Describe(code);
        diagnostics.Record(DiagnosticOperation.State, code: code);
        if (!IsDisposed) Render();
    }

    private string? SelectFile(FilePurpose purpose)
    {
        try { return pickers.Select(this, purpose); }
        catch { ReportError("FILE_READ_FAILED"); return null; }
    }

    private void Render()
    {
        if (IsDisposed || Disposing) return;
        var snapshot = coordinator.Snapshot();
        var online = mode.SelectedIndex != 1;
        onlinePanel.Visible = online; offlinePanel.Visible = !online;
        policyNotice.Text = online ? "网络失败不延长租约；每次操作重新校验。" : "离线不联网；后台撤销的可知延迟最长为剩余离线期限。";
        foreach (var input in new[] { apiUrl, appCode, clientVersion, deviceId, deviceLabel }) input.ReadOnly = snapshot.Authorized || snapshot.IsBusy;
        activate.Enabled = online && !snapshot.IsBusy && !OperationPending;
        trial.Enabled = online && !snapshot.IsBusy && !OperationPending;
        heartbeat.Enabled = online && snapshot.Authorized && !snapshot.IsBusy;
        logout.Enabled = online; // Also cancels an activation which has not returned a token yet.
        release.Enabled = online && snapshot.Authorized && !snapshot.IsBusy;
        versionCheck.Enabled = online && !snapshot.IsBusy && !OperationPending;
        automatic.Enabled = online;
        verifyOffline.Enabled = !online && !snapshot.IsBusy && !OperationPending && offlineLicensePath is not null && trustedKeysPath is not null;
        chooseLicense.Enabled = !online && !snapshot.IsBusy; chooseKeys.Enabled = !online && !snapshot.IsBusy;
        basic.Enabled = snapshot.Authorized;
        export.Enabled = snapshot.CanExport;
        licenseName.Text = offlineLicensePath is null ? "未选择 .kglicense 文件" : Path.GetFileName(offlineLicensePath);
        keysName.Text = trustedKeysPath is null ? "未选择公钥 JSON" : Path.GetFileName(trustedKeysPath);
        var remaining = snapshot.Remaining;
        var text = new StringBuilder();
        text.Append("授权状态：").AppendLine(snapshot.Authorized ? online ? "在线有效" : "离线有效" : "未授权 / 已停止");
        text.Append("授权 ID：").AppendLine(snapshot.LicenseId?.ToString() ?? "—");
        text.Append("授权类型：").Append(snapshot.LicenseId is null ? "—" : !online ? "离线正式授权" : snapshot.IsTrial ? "试用" : "正式");
        text.Append("    功能：").AppendLine(snapshot.Features.Length == 0 ? "无命名功能" : string.Join(", ", snapshot.Features));
        text.Append("授权到期：").AppendLine(Date(snapshot.ExpiresAt));
        text.Append("会话绝对到期：").AppendLine(Date(snapshot.AbsoluteExpiresAt));
        if (snapshot.Version is { } policy)
        {
            text.Append("最低版本：").Append(policy.MinimumVersion ?? "未设置").Append("    最新版本：").Append(policy.LatestVersion ?? "未设置").Append("    强制最低版本：").AppendLine(policy.EnforceMinimum ? "是" : "否");
            if (!string.IsNullOrEmpty(policy.UpdateUrl)) text.Append("升级地址：").AppendLine(policy.UpdateUrl);
            if (!string.IsNullOrEmpty(policy.Sha256)) text.Append("SHA-256：").AppendLine(policy.Sha256);
            if (!string.IsNullOrEmpty(policy.ReleaseNotes)) text.Append("升级说明：").AppendLine(policy.ReleaseNotes[..Math.Min(policy.ReleaseNotes.Length, 4000)]);
        }
        var value = text.ToString();
        if (status.Text != value) status.Text = value;
        protectedReason.Text = !snapshot.Authorized ? ErrorCatalog.Describe(snapshot.PermissionCode) : snapshot.CanExport
            ? "基础操作与 CSV 导出可用；CSV 只包含演示数据。" : "基础操作可用；当前授权没有 export 功能，CSV 导出不可用。";
        operationMessage.Text = messageOverride ?? (snapshot.LastResult.Code == "BASIC_COMPLETED" && snapshot.LastResult.DemoValue is { } calculated
            ? $"基础操作完成：演示数据 12 + 14 + 16 = {calculated}。" : ErrorCatalog.Describe(snapshot.LastResult.Code));
        operationMessage.ForeColor = messageOverride is not null || !snapshot.LastResult.Success ? Color.FromArgb(146, 91, 0) : Color.FromArgb(21, 128, 61);
        var countdown = $"{(online ? "租约" : "离线")}剩余 {(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        progress.Text = countdown + " · " + (snapshot.IsBusy || OperationPending ? "请求处理中… 可清除本地授权或切换模式取消。" : "就绪 · 卡密和令牌不自动保存，诊断不含秘密。");
        if (renderedLogRevision != diagnostics.Revision)
        {
            renderedLogRevision = diagnostics.Revision;
            logList.BeginUpdate(); logList.Items.Clear();
            foreach (var entry in diagnostics.Snapshot()) logList.Items.Add(new ListViewItem([
                entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"), SafeDiagnostics.OperationLabel(entry.Operation), entry.Method + " " + entry.Path,
                entry.StatusCode?.ToString() ?? "—", entry.DurationMilliseconds.ToString(), entry.Code]));
            logList.EndUpdate();
        }
    }

    private LinkLabel HelpLink(string title, string relativePath)
    {
        var link = new LinkLabel { Text = title, AutoSize = true, Margin = new(4, 4, 24, 4), AccessibleName = title };
        link.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, relativePath)) { UseShellExecute = true }); }
            catch { ReportError("FILE_READ_FAILED"); }
        };
        return link;
    }

    private static string Date(DateTimeOffset? value) => value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "—";
    private static TextBox Input(string name, string accessibleName) => new() { Name = name, AccessibleName = accessibleName, Dock = DockStyle.Bottom, Margin = new(6), MaxLength = name == "licenseKey" ? 1024 : 2048 };
    private static Label TextLabel(string name, string text, int width) => new() { Name = name, Text = text, Width = width, Height = 28, AutoEllipsis = true, Margin = new(8, 7, 4, 0), AccessibleName = name };
    private static Button ActionButton(string name, string text, int width, bool primary = false) => new()
    {
        Name = name, Text = text, AccessibleName = text, Width = width, Height = 34, Margin = new(4, 2, 8, 4),
        BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = !primary,
        FlatStyle = primary ? FlatStyle.Flat : FlatStyle.Standard, TabStop = true
    };
    private static GroupBox Group(string text) => new() { Text = text, Dock = DockStyle.Fill, BackColor = Color.White, Padding = new(8, 4, 8, 6), Margin = new(0, 4, 0, 6), TabStop = false };
    private static FlowLayoutPanel Row() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Dock = DockStyle.Top, Margin = new(0), Padding = new(0, 2, 0, 2) };
    private static FlowLayoutPanel VerticalFlow(string name) => new() { Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(0), Padding = new(0) };
    private static Control Field(string label, TextBox input, int tabIndex)
    {
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new(6, 0, 6, 4), TabIndex = tabIndex };
        field.ColumnStyles.Add(new(SizeType.Percent, 100));
        field.RowStyles.Add(new(SizeType.AutoSize)); field.RowStyles.Add(new(SizeType.AutoSize));
        field.Controls.Add(new Label { Name = input.Name + "Label", Text = label, AutoSize = true, Dock = DockStyle.Fill, Margin = new(0) }, 0, 0);
        input.TabIndex = 0; input.Dock = DockStyle.Top; input.Margin = new(0, 2, 0, 0);
        field.Controls.Add(input, 0, 1); return field;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            refreshTimer.Stop(); refreshTimer.Dispose(); lifetime.Cancel(); coordinator.Dispose(); lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
