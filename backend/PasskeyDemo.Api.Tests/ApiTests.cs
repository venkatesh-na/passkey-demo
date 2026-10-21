using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PasskeyDemo.Api.Tests;

public sealed class ApiTests
{
    private static HttpClient Client(WebApplicationFactory<Program> app, bool cookies = true) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost:7043"), HandleCookies = cookies, AllowAutoRedirect = false });

    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }

    [Fact]
    public async Task PasswordCookieLogoutAndReplayProtection()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password", new { username = "alice", password = "AliceDemo!123" })).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/password", new { username = "alice", password = "wrong" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/password", new { username = "alice", password = "AliceDemo!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = login.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("__Host-passkey-session="));
        Assert.Contains("secure", setCookie);
        Assert.Contains("httponly", setCookie);
        Assert.Contains("samesite=strict", setCookie);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        // The anonymous antiforgery token must not be accepted after sign-in.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        using var replay = Client(app, false);
        replay.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
        // Use a LIVE session to prove that a separate API instance cannot restore it.
        await Csrf(client);
        var freshLogin = await client.PostAsJsonAsync("/api/auth/password", new { username = "alice", password = "AliceDemo!123" });
        Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var freshCookie = freshLogin.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("__Host-passkey-session="));
        await using var restarted = new WebApplicationFactory<Program>();
        using var afterRestart = Client(restarted, false);
        afterRestart.DefaultRequestHeaders.Add("Cookie", freshCookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await afterRestart.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RegistrationRequiresSessionAndCorsAllowsOnlyConfiguredFrontend()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = Client(app);
        await Csrf(client);
        var availability = await client.GetFromJsonAsync<JsonElement>("/api/passkeys/authentication/availability");
        Assert.False(availability.GetProperty("hasPasskeys").GetBoolean());
        Assert.Single(availability.EnumerateObject());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/passkeys/registration/options", new { name = "Laptop" })).StatusCode);
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/auth/password");
        allowed.Headers.Add("Origin", "https://localhost:4200");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await client.SendAsync(allowed);
        Assert.Equal("https://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/auth/password");
        denied.Headers.Add("Origin", "https://untrusted.example");
        denied.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.False((await client.SendAsync(denied)).Headers.Contains("Access-Control-Allow-Origin"));
    }
}
