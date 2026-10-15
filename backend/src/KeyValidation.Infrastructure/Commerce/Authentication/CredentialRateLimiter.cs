using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class CredentialRateLimiter(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (DateTimeOffset Start, int Count)> windows = [];
    public bool Admit(Guid credentialId)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (!windows.TryGetValue(credentialId, out var window) || now >= window.Start.AddMinutes(1))
                window = (now, 0);
            if (window.Count >= 60)
                return false;
            windows[credentialId] = (window.Start, window.Count + 1);
            if (windows.Count > 10000)
                foreach (var id in windows.Where(x => now >= x.Value.Start.AddMinutes(1)).Select(x => x.Key).ToArray())
                    windows.Remove(id);
            return true;
        }
    }
}
