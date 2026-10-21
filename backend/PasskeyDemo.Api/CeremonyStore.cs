using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace PasskeyDemo.Api;

public sealed record PendingCeremony(string Kind, string Binding, string? UserId, string? SessionId,
    string? Name, string OptionsJson, DateTimeOffset ExpiresAt);

public sealed class CeremonyStore(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, PendingCeremony> pending = new();
    public string Add(PendingCeremony ceremony)
    {
        foreach (var entry in pending)
            if (entry.Value.ExpiresAt <= clock.GetUtcNow()) pending.TryRemove(entry.Key, out _);
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        pending[id] = ceremony;
        return id;
    }

    public PendingCeremony? Take(string id, string kind, string binding, string? userId = null, string? sessionId = null)
    {
        // Atomic removal BEFORE verification: failed attempts also consume the challenge.
        if (!pending.TryRemove(id, out var value) || value.ExpiresAt <= clock.GetUtcNow()
            || value.Kind != kind || value.Binding != binding || value.UserId != userId || value.SessionId != sessionId)
            return null;
        return value;
    }
}
