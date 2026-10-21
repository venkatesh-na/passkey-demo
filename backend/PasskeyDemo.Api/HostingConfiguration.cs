using Microsoft.AspNetCore.HttpOverrides;

namespace PasskeyDemo.Api;

public static class HostingConfiguration
{
    public static bool Configure(WebApplicationBuilder builder)
    {
        var onRender = string.Equals(builder.Configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase);
        var publicOrigin = builder.Configuration["PUBLIC_ORIGIN"]
            ?? (onRender ? builder.Configuration["RENDER_EXTERNAL_URL"] : null);

        if (onRender && string.IsNullOrWhiteSpace(publicOrigin))
            throw new InvalidOperationException("Render must provide RENDER_EXTERNAL_URL, or set PUBLIC_ORIGIN to the public HTTPS URL.");

        if (!string.IsNullOrWhiteSpace(publicOrigin))
        {
            if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https"
                || origin.GetLeftPart(UriPartial.Authority) != publicOrigin || !string.IsNullOrEmpty(origin.UserInfo))
                throw new InvalidOperationException("PUBLIC_ORIGIN must be an exact HTTPS origin with no path or trailing slash.");
            builder.Configuration["WebAuthn:FrontendOrigin"] = publicOrigin;
            builder.Configuration["WebAuthn:RpId"] = origin.Host;
            builder.Configuration["AllowedHosts"] = origin.Host;
        }

        if (onRender)
        {
            var port = builder.Configuration["PORT"] ?? "10000";
            if (!int.TryParse(port, out var portNumber) || portNumber is < 1 or > 65535)
                throw new InvalidOperationException("PORT must be a valid TCP port.");
            builder.WebHost.UseUrls($"http://0.0.0.0:{portNumber}");
            builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                // Only enabled inside Render's network, behind its mandatory ingress proxy.
                // Do not use this trust configuration for an internet-exposed standalone container.
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                // Do not trust X-Forwarded-Host; AllowedHosts remains the exact configured host.
            });
        }
        return onRender;
    }
}
