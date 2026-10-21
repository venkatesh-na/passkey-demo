using PasskeyDemo.Api;
using Xunit;

namespace PasskeyDemo.Api.Tests;

public sealed class StoreTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void ChallengesAreSingleUseEvenWhenSubmittedConcurrently()
    {
        var clock = new Clock();
        var store = new CeremonyStore(clock);
        var id = store.Add(new("login", "browser", null, null, null, "{}", clock.Now.AddMinutes(2)));
        var results = Enumerable.Range(0, 20).AsParallel().Select(_ => store.Take(id, "login", "browser")).ToArray();
        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public void ChallengeExpiresAtTheDeadline()
    {
        var clock = new Clock();
        var store = new CeremonyStore(clock);
        var id = store.Add(new("login", "browser", null, null, null, "{}", clock.Now.AddSeconds(120)));
        clock.Now = clock.Now.AddSeconds(120);
        Assert.Null(store.Take(id, "login", "browser"));
    }

    [Theory]
    [InlineData("other-browser", "alice", "session", "register")]
    [InlineData("browser", "bob", "session", "register")]
    [InlineData("browser", "alice", "new-session", "register")]
    [InlineData("browser", "alice", "session", "login")]
    public void ChallengeIsBoundToBrowserUserSessionAndPurpose(string binding, string user, string session, string kind)
    {
        var clock = new Clock();
        var store = new CeremonyStore(clock);
        var id = store.Add(new("register", "browser", "alice", "session", "Laptop", "{}", clock.Now.AddMinutes(2)));
        Assert.Null(store.Take(id, kind, binding, user, session));
        Assert.Null(store.Take(id, "register", "browser", "alice", "session"));
    }

    [Fact]
    public void PasskeyOwnershipDeletionAndPasswordFallbackAreIndependent()
    {
        var store = new DemoStore();
        var alice = store.VerifyPassword("alice", "AliceDemo!123")!;
        var bob = store.VerifyPassword("bob", "BobDemo!123")!;
        var key = new StoredPasskey("a2V5", alice.Id, "Laptop", [1, 2], alice.Handle, 0, [], DateTimeOffset.UtcNow);
        Assert.False(store.HasPasskeys);
        Assert.True(store.Add(key));
        Assert.True(store.HasPasskeys);
        Assert.False(store.Add(key));
        Assert.Empty(store.List(bob.Id));
        Assert.False(store.Remove(key.Id, bob.Id));
        Assert.True(store.RecordUse(key, 1, DateTimeOffset.UtcNow));
        Assert.False(store.RecordUse(key, 2, DateTimeOffset.UtcNow));
        Assert.True(store.Remove(key.Id, alice.Id));
        Assert.False(store.HasPasskeys);
        Assert.False(store.RecordUse(store.Find(key.Id) ?? key, 3, DateTimeOffset.UtcNow));
        Assert.NotNull(store.VerifyPassword("ALICE", "AliceDemo!123"));
        Assert.Null(store.VerifyPassword("alice", "wrong"));
    }
}
