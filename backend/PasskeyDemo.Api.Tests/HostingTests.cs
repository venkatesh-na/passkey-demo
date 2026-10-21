using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PasskeyDemo.Api;
using Xunit;

namespace PasskeyDemo.Api.Tests;

public sealed class HostingTests
{
    [Fact]
    public void RenderUsesItsPublicOriginPortAndIngressHeaders()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RENDER"] = "true", ["RENDER_EXTERNAL_URL"] = "https://demo.onrender.com", ["PORT"] = "10000"
        });
        Assert.True(HostingConfiguration.Configure(builder));
        Assert.Equal("https://demo.onrender.com", builder.Configuration["WebAuthn:FrontendOrigin"]);
        Assert.Equal("demo.onrender.com", builder.Configuration["WebAuthn:RpId"]);
        Assert.Equal("demo.onrender.com", builder.Configuration["AllowedHosts"]);
        Assert.Equal("http://0.0.0.0:10000", builder.Configuration["urls"]);
        using var app = builder.Build();
        var forwarded = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, forwarded.ForwardedHeaders);
        Assert.Equal(1, forwarded.ForwardLimit);
    }

    [Theory]
    [InlineData("http://demo.example")]
    [InlineData("https://demo.example/")]
    [InlineData("https://demo.example/profile")]
    [InlineData("https://user:password@demo.example")]
    public void DeploymentRequiresAnExactHttpsOrigin(string origin)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["PUBLIC_ORIGIN"] = origin;
        Assert.Throws<InvalidOperationException>(() => HostingConfiguration.Configure(builder));
    }

    [Fact]
    public async Task PackagedFrontendSupportsDeepLinksWithoutMaskingApiErrors()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "passkey-demo-hosting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Packaged Angular</title>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "main.js"), "console.log('frontend');");
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost:7043") });
            Assert.Contains("Packaged Angular", await client.GetStringAsync("/"));
            Assert.Contains("Packaged Angular", await client.GetStringAsync("/profile"));
            Assert.Contains("frontend", await client.GetStringAsync("/main.js"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/missing")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        }
        finally
        {
            // Delete only the two fixture files in the uniquely-created test directory.
            File.Delete(Path.Combine(webRoot, "index.html"));
            File.Delete(Path.Combine(webRoot, "main.js"));
            Directory.Delete(webRoot);
        }
    }
}
