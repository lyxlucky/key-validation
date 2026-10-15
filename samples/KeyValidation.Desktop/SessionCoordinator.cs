using System.Text.Json;
using KeyValidation.Client;

namespace KeyValidation.Desktop;

public sealed record OperationResult(bool Success, string Code, int? DemoValue = null);

public sealed record SessionSnapshot(AuthorizationMode Mode, bool IsBusy, bool Authorized, bool CanExport,
    bool AutomaticHeartbeat, TimeSpan Remaining, Guid? LicenseId, bool IsTrial, DateTimeOffset? ExpiresAt,
    DateTimeOffset? AbsoluteExpiresAt, string[] Features, ClientVersionInfo? Version, OperationResult LastResult,
    string PermissionCode)
{
    public DateTimeOffset? LastVerifiedAt { get; init; }
}

/// <summary>Owns the authorization boundary; controls only display snapshots of this live state.</summary>
public sealed class SessionCoordinator : IDisposable
{
    private readonly object gate = new();
    private readonly Func<DemoProfile, HttpClient> clientFactory;
    private readonly ITrustedTimeStore timeStore;
    private readonly TimeProvider clock;
    private readonly LeaseTracker lease;
    private readonly LicenseCapabilities capabilities;
    private DemoProfile profile = new();
    private HttpClient? http;
    private LeaseGrant? grant;
    private OfflineLicenseSession? offline;
    private ClientVersionInfo? version;
    private CancellationTokenSource contextCancellation = new();
    private long generation;
    private long heartbeatStarted;
    private bool busy;
    private bool disposed;
    private bool automaticHeartbeat = true;
    private OperationResult lastResult = new(false, "AUTHORIZATION_REQUIRED");
    private DateTimeOffset? lastVerifiedAt;

    public SessionCoordinator(Func<DemoProfile, HttpClient> clientFactory, ITrustedTimeStore timeStore,
        TimeProvider? timeProvider = null)
    {
        this.clientFactory = clientFactory;
        this.timeStore = timeStore;
        clock = timeProvider ?? TimeProvider.System;
        lease = new(clock);
        capabilities = new(lease);
    }

    public void Configure(DemoProfile configuration)
    {
        configuration.Validate();
        lock (gate)
        {
            if (disposed) return;
            EndContext();
            http?.Dispose();
            http = null;
            profile = configuration;
            version = null;
            lastResult = new(false, "CONFIGURATION_LOADED");
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            if (disposed) return;
            EndContext();
            lastResult = new(true, "LOCAL_CLEARED");
        }
    }

    public SessionSnapshot Snapshot()
    {
        lock (gate)
        {
            var denied = Demand();
            var remaining = TimeSpan.Zero;
            if (denied is null)
            {
                try { remaining = offline?.Remaining ?? lease.Remaining; }
                catch (OfflineLicenseException error)
                {
                    RevokeGrants();
                    denied = SetResult(new(false, error.Code));
                }
            }
            var features = offline?.Features ?? grant?.Features?.ToArray() ?? [];
            return new(profile.Mode, busy, denied is null, denied is null && features.Contains("export", StringComparer.Ordinal),
                automaticHeartbeat, remaining, offline?.LicenseId ?? grant?.LicenseId,
                grant?.IsTrial ?? false, offline?.ExpiresAt ?? grant?.ExpiresAt, grant?.AbsoluteExpiresAt, features,
                version, lastResult, denied?.Code ?? "AUTHORIZED") { LastVerifiedAt = lastVerifiedAt };
        }
    }

    public Task<OperationResult> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default) =>
        ExecuteLeaseAsync(false, licenseKey, cancellationToken);

    public Task<OperationResult> HeartbeatAsync(CancellationToken cancellationToken = default) =>
        ExecuteLeaseAsync(true, null, cancellationToken);

    public Task<OperationResult> ActivateTrialAsync(CancellationToken cancellationToken = default) => ExecuteLeaseAsync(false, null, cancellationToken, trial: true);
    public async Task<OperationResult> CheckVersionAsync(CancellationToken cancellationToken = default)
    {
        RequestScope request;
        lock (gate)
        {
            if (UnavailableForOnline() is { } unavailable) return unavailable;
            request = BeginRequest(cancellationToken);
        }
        try
        {
            var policy = await DesktopProtocol.CheckVersionAsync(request.Http!, request.Profile, request.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                request.Cancellation.Token.ThrowIfCancellationRequested();
                version = policy;
                return SetResult(new(true, "VERSION_CHECKED"));
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                if (error is LicenseRejectedException rejection)
                {
                    RevokeGrants();
                    return SetResult(new(false, ErrorCatalog.SafeCode(rejection.Message)));
                }
                return SetResult(new(false, error is OperationCanceledException ? "NETWORK_TIMEOUT" :
                    error is JsonException or InvalidDataException ? "INVALID_SERVER_RESPONSE" : "NETWORK_UNAVAILABLE"));
            }
        }
        finally
        {
            lock (gate) { if (IsCurrent(request)) busy = false; request.Dispose(); }
        }
    }
    public Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default) => EndRemoteAsync(false, cancellationToken);
    public Task<OperationResult> ReleaseDeviceAsync(CancellationToken cancellationToken = default) => EndRemoteAsync(true, cancellationToken);
    private async Task<OperationResult> EndRemoteAsync(bool release, CancellationToken cancellationToken)
    {
        RequestScope request;
        lock (gate)
        {
            if (disposed) return new(false, "LOCAL_CLOSED");
            if (release && profile.Mode == AuthorizationMode.Offline) return SetResult(new(false, "OFFLINE_NETWORK_DISABLED"));
            var token = grant?.SessionToken;
            EndContext(); // Local permission ends before any remote wait, including cancellation/failure.
            if (token is null) return SetResult(new(true, "LOGOUT_LOCAL_ONLY"));
            request = BeginRequest(cancellationToken) with { SessionToken = token };
        }
        try
        {
            await DesktopProtocol.CheckedCommandAsync(request.Http!, release ? "/api/client/device-bindings/release" : "/api/client/sessions/logout",
                request.SessionToken!, request.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                request.Cancellation.Token.ThrowIfCancellationRequested();
                return SetResult(new(true, release ? "DEVICE_RELEASED" : "LOGOUT_CONFIRMED"));
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                return SetResult(new(false, release && error is LicenseRejectedException rejection
                    ? ErrorCatalog.SafeCode(rejection.Message) : release ? "RELEASE_UNCONFIRMED" : "LOGOUT_UNCONFIRMED"));
            }
        }
        finally
        {
            lock (gate) { if (IsCurrent(request)) busy = false; request.Dispose(); }
        }
    }

    public async Task<OperationResult> ValidateOfflineAsync(string licensePath, string trustedKeysPath, CancellationToken cancellationToken = default)
    {
        RequestScope request;
        lock (gate)
        {
            if (disposed) return new(false, "LOCAL_CLOSED");
            if (profile.Mode != AuthorizationMode.Offline) return SetResult(new(false, "OFFLINE_MODE_REQUIRED"));
            if (busy) return new(false, "REQUEST_IN_PROGRESS");
            EndContext();
            request = BeginRequest(cancellationToken, needsHttp: false);
        }
        try
        {
            var token = request.Cancellation.Token;
            var session = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var keys = TrustedOfflineKeys.Load(trustedKeysPath);
                using var input = File.OpenRead(licensePath);
                if (input.Length is < 100 or > 16384) throw new OfflineLicenseException("OFFLINE_LICENSE_INVALID");
                using var reader = new StreamReader(input);
                var content = reader.ReadToEnd().Trim();
                token.ThrowIfCancellationRequested();
                return new OfflineLicenseVerifier(keys, timeStore, clock).Open(content, request.Profile.AppCode, request.Profile.DeviceId);
            }, token).WaitAsync(token).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                token.ThrowIfCancellationRequested();
                offline = session;
                return SetResult(new(true, "OFFLINE_VERIFIED"));
            }
        }
        catch (Exception error) when (error is OfflineLicenseException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                RevokeGrants();
                return SetResult(new(false, error is OfflineLicenseException offlineError ? offlineError.Code :
                    error is OperationCanceledException ? "REQUEST_CANCELLED" : "FILE_READ_FAILED"));
            }
        }
        finally
        {
            lock (gate)
            {
                if (IsCurrent(request)) busy = false;
                request.Dispose();
            }
        }
    }

    private async Task<OperationResult> ExecuteLeaseAsync(bool heartbeat, string? licenseKey, CancellationToken cancellationToken, bool trial = false)
    {
        RequestScope request;
        lock (gate)
        {
            if (UnavailableForOnline() is { } unavailable) return unavailable;
            if (heartbeat && Demand() is { } denied) return SetResult(denied);
            if (!heartbeat && !trial && string.IsNullOrWhiteSpace(licenseKey)) return SetResult(new(false, "LICENSE_KEY_REQUIRED"));
            if (!heartbeat) EndContext();
            request = BeginRequest(cancellationToken);
        }
        try
        {
            var api = request.Profile.ClientVersion.Length == 0 ? new LicenseApi(request.Http!) : new LicenseApi(request.Http!, request.Profile.ClientVersion);
            var response = heartbeat
                ? await api.HeartbeatAsync(request.SessionToken!, request.Cancellation.Token).ConfigureAwait(false)
                : trial ? await api.ActivateTrialAsync(request.Profile.AppCode, request.Profile.DeviceId, request.Profile.DeviceLabel, request.Cancellation.Token).ConfigureAwait(false)
                : await api.ActivateAsync(request.Profile.AppCode, licenseKey!, request.Profile.DeviceId, request.Profile.DeviceLabel, request.Cancellation.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                request.Cancellation.Token.ThrowIfCancellationRequested();
                ApplyGrant(response, request, heartbeat);
                return SetResult(Demand() ?? new(true, heartbeat ? "HEARTBEAT_CONFIRMED" : trial ? "TRIAL_ACTIVATED" : "ACTIVATED"));
            }
        }
        catch (Exception error) when (IsExpected(error))
        {
            lock (gate)
            {
                if (!IsCurrent(request)) return Superseded();
                if (error is LicenseRejectedException rejection)
                {
                    RevokeGrants();
                    version = rejection.Version;
                    return SetResult(new(false, ErrorCatalog.SafeCode(rejection.Message)));
                }
                if (error is JsonException or InvalidDataException)
                {
                    RevokeGrants();
                    return SetResult(new(false, "INVALID_SERVER_RESPONSE"));
                }
                return SetResult(new(false, error is OperationCanceledException
                    ? cancellationToken.IsCancellationRequested ? "REQUEST_CANCELLED" : "NETWORK_TIMEOUT"
                    : "NETWORK_UNAVAILABLE"));
            }
        }
        finally
        {
            lock (gate)
            {
                if (IsCurrent(request)) { busy = false; heartbeatStarted = clock.GetTimestamp(); }
                request.Dispose();
            }
        }
    }

    private void ApplyGrant(LeaseGrant response, RequestScope request, bool heartbeat)
    {
        var token = response.SessionToken ?? (heartbeat ? request.SessionToken : null);
        if (string.IsNullOrWhiteSpace(token) || response.ServerTime == default || response.ExpiresAt == default ||
            response.LeaseExpiresAt == default || response.AbsoluteExpiresAt == default || response.HeartbeatIntervalSeconds is < 1 or > 86400)
            throw new InvalidDataException();
        grant = response with { SessionToken = token };
        lease.Update(response.ServerTime, response.LeaseExpiresAt < response.ExpiresAt ? response.LeaseExpiresAt : response.ExpiresAt,
            response.AbsoluteExpiresAt, clock.GetElapsedTime(request.Started));
        capabilities.Update(response.Features);
        version = response.Version;
        lastVerifiedAt = clock.GetUtcNow();
    }

    public void SetAutomaticHeartbeat(bool enabled)
    {
        lock (gate)
        {
            if (disposed) return;
            if (enabled && !automaticHeartbeat) heartbeatStarted = clock.GetTimestamp();
            automaticHeartbeat = enabled;
        }
    }

    public Task<OperationResult> TickAsync()
    {
        lock (gate)
        {
            if (disposed || profile.Mode != AuthorizationMode.Online || busy || !automaticHeartbeat || grant is null ||
                !lease.IsValid || clock.GetElapsedTime(heartbeatStarted) < TimeSpan.FromSeconds(grant.HeartbeatIntervalSeconds))
                return Task.FromResult(new OperationResult(true, "IDLE"));
        }
        return HeartbeatAsync();
    }

    public OperationResult PerformBasicAction()
    {
        lock (gate)
        {
            if (Demand() is { } denied) return SetResult(denied);
            int[] demonstrationValues = [12, 14, 16];
            return SetResult(new(true, "BASIC_COMPLETED", demonstrationValues.Sum()));
        }
    }

    public OperationResult ExportCsv(string path)
    {
        lock (gate)
        {
            // The file chooser belongs outside this method. This final check is immediately before the write.
            if (Demand("export") is { } denied) return SetResult(denied);
            try
            {
                File.WriteAllText(path, "item,quantity\n演示项目,1\n", new System.Text.UTF8Encoding(true));
                return SetResult(new(true, "EXPORT_COMPLETED"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { return SetResult(new(false, "FILE_WRITE_FAILED")); }
        }
    }

    private OperationResult? Demand(string? feature = null)
    {
        if (disposed) return new(false, "LOCAL_CLOSED");
        if (offline is not null)
        {
            try { offline.Demand(feature); return null; }
            catch (OfflineLicenseException error)
            {
                if (error.Code != "FEATURE_NOT_GRANTED") RevokeGrants();
                return SetResult(new(false, error.Code));
            }
        }
        if (grant is null) return new(false, "AUTHORIZATION_REQUIRED");
        if (!lease.IsValid)
        {
            // The effective deadline is measured monotonically by LeaseTracker.
            // Distinguish the server's limiting deadline without trusting the PC clock.
            return new(false, grant.ExpiresAt <= grant.LeaseExpiresAt && grant.ExpiresAt <= grant.AbsoluteExpiresAt
                ? "LICENSE_EXPIRED" : grant.AbsoluteExpiresAt <= grant.LeaseExpiresAt ? "SESSION_EXPIRED" : "LEASE_EXPIRED");
        }
        if (feature is not null && !capabilities.Allows(feature)) return new(false, "FEATURE_NOT_GRANTED");
        return null;
    }

    private OperationResult? UnavailableForOnline()
    {
        if (disposed) return new(false, "LOCAL_CLOSED");
        if (profile.Mode != AuthorizationMode.Online) return SetResult(new(false, "OFFLINE_NETWORK_DISABLED"));
        return busy ? new(false, "REQUEST_IN_PROGRESS") : null;
    }

    private RequestScope BeginRequest(CancellationToken cancellationToken, bool needsHttp = true)
    {
        if (needsHttp) http ??= clientFactory(profile);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(contextCancellation.Token, cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        busy = true;
        return new(generation, profile, needsHttp ? http : null, linked, clock.GetTimestamp(), grant?.SessionToken);
    }

    private bool IsCurrent(RequestScope request) => !disposed && request.Generation == generation;
    private static OperationResult Superseded() => new(false, "REQUEST_SUPERSEDED");
    private OperationResult SetResult(OperationResult result) => lastResult = result;
    private static bool IsExpected(Exception error) => error is LicenseRejectedException or HttpRequestException or
        OperationCanceledException or JsonException or InvalidDataException;

    private void RevokeGrants()
    {
        lastVerifiedAt = null;
        grant = null;
        offline = null;
        capabilities.Update([]);
        lease.Update(clock.GetUtcNow(), clock.GetUtcNow(), clock.GetUtcNow(), TimeSpan.Zero);
    }

    private void EndContext()
    {
        generation++;
        contextCancellation.Cancel();
        contextCancellation.Dispose();
        contextCancellation = new();
        busy = false;
        RevokeGrants();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            EndContext();
            disposed = true;
            contextCancellation.Dispose();
            http?.Dispose();
            http = null;
        }
    }

    private sealed record RequestScope(long Generation, DemoProfile Profile, HttpClient? Http,
        CancellationTokenSource Cancellation, long Started, string? SessionToken) : IDisposable
    {
        public void Dispose() => Cancellation.Dispose();
    }
}
