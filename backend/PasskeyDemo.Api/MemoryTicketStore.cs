using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace PasskeyDemo.Api;

// The browser holds an encrypted opaque ticket key. All authentication state lives in memory.
// API restarts invalidate every session, even when Data Protection keys persist on disk.
public sealed class MemoryTicketStore(IMemoryCache cache) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "ticket:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await RenewAsync(key, ticket);
        return key;
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        cache.Set(key, ticket, ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(30));
        return Task.CompletedTask;
    }
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(cache.Get<AuthenticationTicket>(key));
    public Task RemoveAsync(string key) { cache.Remove(key); return Task.CompletedTask; }
}
