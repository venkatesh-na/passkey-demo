using Fido2NetLib;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using PasskeyDemo.Api;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var onRender = HostingConfiguration.Configure(builder);
var settings = builder.Configuration.GetSection("WebAuthn").Get<WebAuthnSettings>() ?? new();
var origin = new Uri(settings.FrontendOrigin);
if (origin.Scheme != "https" || origin.GetLeftPart(UriPartial.Authority) != settings.FrontendOrigin
    || !(origin.Host == settings.RpId || origin.Host.EndsWith("." + settings.RpId, StringComparison.Ordinal))
    || settings.ChallengeLifetimeSeconds is < 1 or > 300)
    throw new InvalidOperationException("Use an exact HTTPS frontend origin, a matching RP host (no scheme/port), and a challenge lifetime of 1–300 seconds.");

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<DemoStore>();
builder.Services.AddSingleton<CeremonyStore>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MemoryTicketStore>();
builder.Services.AddSingleton<IFido2>(new Fido2(new Fido2Configuration
{
    RPID = settings.RpId,
    RPName = settings.RpName,
    Origins = new HashSet<string> { settings.FrontendOrigin },
    Timeout = checked((uint)settings.ChallengeLifetimeSeconds * 1000)
}));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(settings.FrontendOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Host-passkey-session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<MemoryTicketStore>((options, store) => options.SessionStore = store);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-passkey-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("demo", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();
if (onRender) app.UseForwardedHeaders();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new ApiError("The API could not complete the request. Please try again."));
}));
app.UseHealthChecks("/healthz");
app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new ApiError("Your security token expired. Refresh the page and try again."));
            return;
        }
    }
    await next(context);
});
app.MapControllers().RequireRateLimiting("demo");
if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    // An unknown API route must return JSON/404 instead of the Angular page.
    app.Map("/api/{**path}", () => Results.NotFound(new ApiError("API endpoint not found.")));
    app.MapFallbackToFile("index.html");
}
app.Run();

public partial class Program;
