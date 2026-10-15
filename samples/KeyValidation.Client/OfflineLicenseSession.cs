namespace KeyValidation.Client;

public sealed class OfflineLicenseSession
{
    private readonly OfflinePayload payload;
    private readonly ITrustedTimeStore store;
    private readonly TimeProvider clock;
    private readonly HashSet<string> features;
    private readonly object gate = new();
    private readonly long startedAt;
    private readonly DateTimeOffset startedUtc;
    private DateTimeOffset latestUtc;
    private string? failure;
    public Guid Serial => payload.Serial;
    public Guid LicenseId => payload.LicenseId;
    public Guid BindingId => payload.BindingId;
    public DateTimeOffset ExpiresAt => payload.ExpiresAt;
    public string[] Features => features.ToArray();
    public TimeSpan Remaining { get { lock (gate) return payload.ExpiresAt - CheckTime(); } }

    internal OfflineLicenseSession(OfflinePayload payload, ITrustedTimeStore store, TimeProvider clock)
    {
        this.payload = payload; this.store = store; this.clock = clock; features = new(payload.Features, StringComparer.Ordinal);
        startedAt = clock.GetTimestamp(); var wall = clock.GetUtcNow();
        var persisted = ReadLatest(); CheckRollback(wall, persisted ?? wall);
        startedUtc = latestUtc = persisted > wall ? persisted.Value : wall;
    }

    // Every protected operation calls Demand immediately before doing its work.
    // A previously returned bool or feature array is not an authorization lease.
    public void Demand(string? feature = null)
    {
        lock (gate)
        {
            CheckTime();
            if (feature is not null && (string.IsNullOrWhiteSpace(feature) || !features.Contains(feature.Trim().ToLowerInvariant()))) throw new OfflineLicenseException("FEATURE_NOT_GRANTED");
        }
    }

    private DateTimeOffset CheckTime()
    {
        if (failure is not null) throw new OfflineLicenseException(failure);
        try
        {
            var wall = clock.GetUtcNow(); var elapsed = clock.GetElapsedTime(startedAt);
            if (elapsed < TimeSpan.Zero) throw new OfflineLicenseException("OFFLINE_CLOCK_ROLLBACK");
            var monotonic = startedUtc + elapsed;
            var current = Max(latestUtc, Max(wall, monotonic));
            var persisted = ReadLatest(); if (persisted is { } trusted) current = Max(current, trusted);
            CheckRollback(wall, current);
            DateTimeOffset saved;
            try { saved = store.SaveLatest(current); }
            catch (Exception error) when (error is not OfflineLicenseException) { throw new OfflineLicenseException("OFFLINE_STATE_UNAVAILABLE"); }
            if (saved < current) throw new OfflineLicenseException("OFFLINE_STATE_UNAVAILABLE");
            // Persist even an observed expiry: a restart and a small tolerated
            // clock correction must not revive an already expired certificate.
            CheckRollback(wall, saved); latestUtc = saved; RequireTime(saved); return saved;
        }
        catch (OfflineLicenseException error) { failure = error.Code; throw; }
        catch (ArgumentOutOfRangeException) { failure = "OFFLINE_CLOCK_ROLLBACK"; throw new OfflineLicenseException(failure); }
    }
    private DateTimeOffset? ReadLatest()
    {
        try { return store.ReadLatest(); }
        catch (Exception error) when (error is not OfflineLicenseException) { throw new OfflineLicenseException("OFFLINE_STATE_UNAVAILABLE"); }
    }
    private void RequireTime(DateTimeOffset time)
    {
        if (time < payload.NotBefore || time < payload.IssuedAt) throw new OfflineLicenseException("OFFLINE_LICENSE_NOT_YET_VALID");
        if (time >= payload.ExpiresAt) throw new OfflineLicenseException("OFFLINE_LICENSE_EXPIRED");
    }
    private static void CheckRollback(DateTimeOffset wall, DateTimeOffset trusted)
    { if (trusted - wall > TimeSpan.FromMinutes(5)) throw new OfflineLicenseException("OFFLINE_CLOCK_ROLLBACK"); }
    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;
}
