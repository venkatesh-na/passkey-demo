using System.Security.Cryptography;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace PasskeyDemo.Api;

public sealed class DemoUser(string username, string displayName)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public byte[] Handle { get; } = RandomNumberGenerator.GetBytes(32);
    public string Username { get; } = username;
    public string DisplayName { get; } = displayName;
    public string PasswordHash { get; set; } = "";
    public Fido2User ToFidoUser() => new() { Id = Handle, Name = Username, DisplayName = DisplayName };
}

public sealed record StoredPasskey(
    string Id, string UserId, string Name, byte[] PublicKey, byte[] UserHandle,
    uint SignCount, AuthenticatorTransport[] Transports, DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt = null)
{
    public PasskeyView ToView() => new(Id, Name, CreatedAt, LastUsedAt);
    public PublicKeyCredentialDescriptor Descriptor => new(PublicKeyCredentialType.PublicKey, WebEncoders.Base64UrlDecode(Id), Transports);
}

// One lock makes uniqueness, ownership, counter updates and deletion atomic.
public sealed class DemoStore
{
    private readonly object gate = new();
    private readonly Dictionary<string, StoredPasskey> credentials = [];
    private readonly DemoUser[] users;
    private readonly PasswordHasher<DemoUser> hasher = new();
    public const int MaximumPasskeys = 10;

    public DemoStore()
    {
        users = [new("alice", "Alice Morgan"), new("bob", "Bob Taylor")];
        foreach (var user in users)
            user.PasswordHash = hasher.HashPassword(user, user.Username == "alice" ? "AliceDemo!123" : "BobDemo!123");
    }

    public DemoUser? FindUser(string id) => users.FirstOrDefault(u => u.Id == id);
    public DemoUser? VerifyPassword(string username, string password)
    {
        var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        // Verify a real hash even for unknown usernames.
        var candidate = user ?? users[0];
        var result = hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, password);
        return user is not null && result != PasswordVerificationResult.Failed ? user : null;
    }

    public StoredPasskey[] List(string userId)
    {
        lock (gate) return credentials.Values.Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt).ToArray();
    }
    public bool HasPasskeys { get { lock (gate) return credentials.Count > 0; } }
    public StoredPasskey? Find(string id) { lock (gate) return credentials.GetValueOrDefault(id); }
    public bool Add(StoredPasskey key)
    {
        lock (gate)
        {
            return credentials.Values.Count(c => c.UserId == key.UserId) < MaximumPasskeys && credentials.TryAdd(key.Id, key);
        }
    }
    public bool Remove(string id, string userId)
    {
        lock (gate) return credentials.TryGetValue(id, out var key) && key.UserId == userId && credentials.Remove(id);
    }
    public bool RecordUse(StoredPasskey snapshot, uint newCount, DateTimeOffset now)
    {
        lock (gate)
        {
            // Reject credentials removed/replaced while verification was in flight.
            if (!credentials.TryGetValue(snapshot.Id, out var current) || !ReferenceEquals(current, snapshot)) return false;
            credentials[snapshot.Id] = current with { SignCount = newCount, LastUsedAt = now };
            return true;
        }
    }
}
