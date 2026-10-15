using KeyValidation.Client;

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    is { Length: > 0 } value ? value : throw new InvalidOperationException($"缺少环境变量 {name}。");

try
{
    using var stopped = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopped.Cancel(); };
    // Select offline mode before constructing any HTTP client or reading any
    // online setting. Missing or invalid offline inputs never fall back online.
    if (Environment.GetEnvironmentVariable("KEYVALIDATION_OFFLINE_FILE") is { } offlineFile)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("KEYVALIDATION_OFFLINE_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
            throw new OfflineLicenseException("OFFLINE_DISABLED");
        if (string.IsNullOrWhiteSpace(offlineFile)) throw new InvalidOperationException("KEYVALIDATION_OFFLINE_FILE 不能为空。");
        try
        {
            await OfflineConsole.RunAsync(offlineFile, Required("KEYVALIDATION_OFFLINE_KEYS_FILE"), Required("KEYVALIDATION_OFFLINE_STATE_FILE"),
                Required("KEYVALIDATION_APP_CODE"), Required("KEYVALIDATION_DEVICE_ID"), Environment.GetEnvironmentVariable("KEYVALIDATION_REQUIRED_FEATURE"), stopped.Token);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        return;
    }
    var baseUri = new Uri(Required("KEYVALIDATION_API_URL"));
    if (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
        throw new InvalidOperationException("远程服务必须使用 HTTPS；HTTP 仅允许本机调试。");
    using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(10) };
    var api = new LicenseApi(http, Environment.GetEnvironmentVariable("KEYVALIDATION_CLIENT_VERSION") ?? "1.0.0");
    var clock = TimeProvider.System;
    var lease = new LeaseTracker(clock);
    var capabilities = new LicenseCapabilities(lease);
    var requiredFeature = Environment.GetEnvironmentVariable("KEYVALIDATION_REQUIRED_FEATURE")?.Trim();
    var useTrial = string.Equals(Environment.GetEnvironmentVariable("KEYVALIDATION_TRIAL"), "true", StringComparison.OrdinalIgnoreCase);
    var started = clock.GetTimestamp();
    var grant = useTrial
        ? await api.ActivateTrialAsync(Required("KEYVALIDATION_APP_CODE"), Required("KEYVALIDATION_DEVICE_ID"), Environment.MachineName, stopped.Token)
        : await api.ActivateAsync(Required("KEYVALIDATION_APP_CODE"), Required("KEYVALIDATION_LICENSE_KEY"),
            Required("KEYVALIDATION_DEVICE_ID"), Environment.MachineName, stopped.Token);
    var token = grant.SessionToken ?? throw new InvalidDataException("服务未返回会话。");
    var nextDelay = TimeSpan.FromSeconds(Math.Clamp(grant.HeartbeatIntervalSeconds, 1, 60));
    try
    {
        UpdateLease(grant, clock.GetElapsedTime(started));
        Console.WriteLine(grant.IsTrial ? "试用激活成功，开始心跳。" : "激活成功，开始心跳。");
        Console.WriteLine("受保护操作前检查 lease.IsValid；功能权限使用 capabilities.Allows(功能标识)。按 Ctrl+C 退出。");
        while (!stopped.IsCancellationRequested && lease.IsValid)
        {
            var delay = nextDelay < lease.Remaining ? nextDelay : lease.Remaining;
            await Task.Delay(delay, stopped.Token);
            if (!lease.IsValid) break;
            using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
            requestDeadline.CancelAfter(lease.Remaining);
            started = clock.GetTimestamp();
            try
            {
                grant = await api.HeartbeatAsync(token, requestDeadline.Token);
                UpdateLease(grant, clock.GetElapsedTime(started));
                nextDelay = TimeSpan.FromSeconds(Math.Clamp(grant.HeartbeatIntervalSeconds, 1, 60));
                Console.WriteLine("心跳验证成功。");
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !stopped.IsCancellationRequested))
            {
                if (!lease.IsValid) break;
                Console.WriteLine("网络暂时不可用，将在当前租约内重试。");
                nextDelay = TimeSpan.FromSeconds(5);
            }
        }
        if (!stopped.IsCancellationRequested)
        {
            Console.WriteLine("授权租约已到期，停止受保护功能。请重新激活。");
            Environment.ExitCode = 2;
        }
    }
    catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
    finally
    {
        using var logoutDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await api.LogoutAsync(token, logoutDeadline.Token); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
    }

    void UpdateLease(LeaseGrant next, TimeSpan duration)
    {
        var cap = next.AbsoluteExpiresAt < next.ExpiresAt ? next.AbsoluteExpiresAt : next.ExpiresAt;
        lease.Update(next.ServerTime, next.LeaseExpiresAt, cap, duration);
        capabilities.Update(next.Features);
        PrintUpdate(next.Version);
        if (!string.IsNullOrEmpty(requiredFeature) && !capabilities.Allows(requiredFeature))
            throw new LicenseRejectedException("FEATURE_NOT_GRANTED");
    }
}
catch (LicenseRejectedException e)
{
    PrintUpdate(e.Version);
    Console.Error.WriteLine($"授权被拒绝（{e.Message}），停止受保护功能。");
    Environment.ExitCode = 2;
}
catch (OfflineLicenseException e)
{
    Console.Error.WriteLine($"离线授权被拒绝（{e.Code}），停止受保护功能。");
    Environment.ExitCode = 2;
}
catch (Exception e) when (e is InvalidOperationException or UriFormatException or InvalidDataException)
{
    Console.Error.WriteLine(e.Message);
    Environment.ExitCode = 1;
}

catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine("无法连接授权服务，未启用受保护功能。");
    Environment.ExitCode = 1;
}

static void PrintUpdate(ClientVersionInfo? version)
{
    if (version is null || !version.UpgradeAvailable && !version.EnforceMinimum) return;
    Console.WriteLine($"客户端版本：{version.ClientVersion ?? "未提供"}；最低版本：{version.MinimumVersion ?? "无限制"}；最新版本：{version.LatestVersion ?? "未发布"}。");
    if (version.UpdateUrl is { Length: > 0 } url) Console.WriteLine($"请从发布方提供的地址获取更新：{url}");
    if (version.Sha256 is { Length: > 0 } checksum) Console.WriteLine($"安装前验证发布方签名及 SHA-256：{checksum}");
    if (version.ReleaseNotes is { Length: > 0 } notes) Console.WriteLine(notes);
}
