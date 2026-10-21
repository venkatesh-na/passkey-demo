using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace PasskeyDemo.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(DemoStore store, IAntiforgery antiforgery) : ControllerBase
{
    internal const string BindingCookie = "__Host-passkey-browser";

    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        if (string.IsNullOrEmpty(Request.Cookies[BindingCookie])) RotateBinding(Response);
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [HttpPost("password")]
    public async Task<IActionResult> Password(PasswordLoginRequest request)
    {
        var user = store.VerifyPassword(request.Username.Trim(), request.Password);
        if (user is null) return Unauthorized(new ApiError("The username or password is incorrect."));
        await CreateSession(HttpContext, user, "Password");
        return Ok(new UserView(user.Username, user.DisplayName, "Password"));
    }

    [Authorize, HttpGet("me")]
    public IActionResult Me()
    {
        var user = store.FindUser(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return user is null ? Unauthorized() : Ok(new UserView(user.Username, user.DisplayName,
            User.FindFirstValue("login_method") ?? "Password"));
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        RotateBinding(Response);
        return NoContent();
    }

    internal static async Task CreateSession(HttpContext context, DemoUser user, string method)
    {
        // Remove a previous server ticket before issuing a new one.
        if (context.User.Identity?.IsAuthenticated == true)
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.Username),
            new Claim("login_method", method), new Claim("session_id", Guid.NewGuid().ToString("N"))
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
        RotateBinding(context.Response);
    }

    private static void RotateBinding(HttpResponse response) => response.Cookies.Append(BindingCookie,
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), new CookieOptions
        { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });
}
