using System.Security.Claims;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace PasskeyDemo.Api.Controllers;

[ApiController, Route("api/passkeys")]
public sealed class PasskeysController(IFido2 fido, DemoStore store, CeremonyStore ceremonies,
    WebAuthnSettings settings, TimeProvider clock, ILogger<PasskeysController> logger) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string? SessionId => User.FindFirstValue("session_id");
    private string Binding => Request.Cookies[AuthController.BindingCookie] ?? "";

    // Anonymous login only needs an aggregate flag, never account or credential details.
    [HttpGet("authentication/availability")]
    public IActionResult AuthenticationAvailability() => Ok(new { hasPasskeys = store.HasPasskeys });

    [Authorize, HttpGet]
    public IActionResult List() => Ok(store.List(UserId).Select(c => c.ToView()));

    [Authorize, HttpDelete("{id}")]
    public IActionResult Delete(string id) => store.Remove(id, UserId) ? NoContent() : NotFound(new ApiError("Passkey not found."));

    [Authorize, HttpPost("registration/options")]
    public IActionResult RegistrationOptions(RegistrationStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new ApiError("Enter a friendly name for this passkey."));
        if (Binding.Length == 0) return BadRequest(new ApiError("Refresh the page before registering a passkey."));
        var user = store.FindUser(UserId);
        if (user is null) return Unauthorized();
        var existing = store.List(UserId);
        if (existing.Length >= DemoStore.MaximumPasskeys) return BadRequest(new ApiError("Remove a passkey before adding another (limit: 10)."));
        var options = fido.RequestNewCredential(new RequestNewCredentialParams
        {
            User = user.ToFidoUser(),
            ExcludeCredentials = existing.Select(c => c.Descriptor).ToArray(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
        var ceremonyId = ceremonies.Add(new("register", Binding, UserId, SessionId, request.Name.Trim(),
            options.ToJson(), clock.GetUtcNow().AddSeconds(settings.ChallengeLifetimeSeconds)));
        return Ok(new { ceremonyId, options });
    }

    [Authorize, HttpPost("registration/verify")]
    public async Task<IActionResult> RegistrationVerify(RegistrationFinishRequest request, CancellationToken ct)
    {
        var pending = ceremonies.Take(request.CeremonyId, "register", Binding, UserId, SessionId);
        if (pending is null) return BadRequest(new ApiError("Registration expired or was already used. Please start again."));
        try
        {
            var result = await fido.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = request.Credential,
                OriginalOptions = CredentialCreateOptions.FromJson(pending.OptionsJson),
                IsCredentialIdUniqueToUserCallback = (args, _) =>
                    Task.FromResult(store.Find(WebEncoders.Base64UrlEncode(args.CredentialId)) is null)
            }, ct);
            var key = new StoredPasskey(WebEncoders.Base64UrlEncode(result.Id), UserId, pending.Name!,
                result.PublicKey, result.User.Id, result.SignCount, result.Transports ?? [], clock.GetUtcNow());
            if (!store.Add(key)) return Conflict(new ApiError("This passkey is already registered, or the account has reached its passkey limit."));
            return Ok(key.ToView());
        }
        catch (Fido2VerificationException ex)
        {
            logger.LogInformation("Registration verification rejected: {Reason}", ex.Message);
            return BadRequest(new ApiError("The passkey could not be verified. Please start registration again."));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return BadRequest(new ApiError("The browser returned an invalid passkey response. Please try again."));
        }
    }

    [HttpPost("authentication/options")]
    public IActionResult AuthenticationOptions()
    {
        if (Binding.Length == 0) return BadRequest(new ApiError("Refresh the page before signing in."));
        var options = fido.GetAssertionOptions(new GetAssertionOptionsParams
        {
            // Empty allowCredentials enables discoverable, username-free sign-in.
            AllowedCredentials = [], UserVerification = UserVerificationRequirement.Required
        });
        var ceremonyId = ceremonies.Add(new("login", Binding, null, null, null, options.ToJson(),
            clock.GetUtcNow().AddSeconds(settings.ChallengeLifetimeSeconds)));
        return Ok(new { ceremonyId, options });
    }

    [HttpPost("authentication/verify")]
    public async Task<IActionResult> AuthenticationVerify(AuthenticationFinishRequest request, CancellationToken ct)
    {
        var pending = ceremonies.Take(request.CeremonyId, "login", Binding);
        if (pending is null) return BadRequest(new ApiError("Sign-in expired or was already used. Please start again."));
        try
        {
            var key = store.Find(WebEncoders.Base64UrlEncode(request.Credential.RawId));
            if (key is null) return BadRequest(new ApiError("This passkey is not registered with this demo. Sign in with your password to add it again."));
            // Discoverable credentials must return the opaque user handle saved at registration.
            var handle = request.Credential.Response.UserHandle;
            if (handle is null || !handle.AsSpan().SequenceEqual(key.UserHandle))
                return BadRequest(new ApiError("The passkey could not be verified. Please try again."));
            var result = await fido.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.Credential,
                OriginalOptions = AssertionOptions.FromJson(pending.OptionsJson),
                StoredPublicKey = key.PublicKey,
                StoredSignatureCounter = key.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                    args.UserHandle.AsSpan().SequenceEqual(key.UserHandle)
                    && args.CredentialId.AsSpan().SequenceEqual(WebEncoders.Base64UrlDecode(key.Id)))
            }, ct);
            var user = store.FindUser(key.UserId);
            if (user is null || !store.RecordUse(key, result.SignCount, clock.GetUtcNow()))
                return BadRequest(new ApiError("The passkey changed during sign-in. Please try again."));
            await AuthController.CreateSession(HttpContext, user, "Passkey");
            return Ok(new UserView(user.Username, user.DisplayName, "Passkey"));
        }
        catch (Fido2VerificationException ex)
        {
            logger.LogInformation("Authentication verification rejected: {Reason}", ex.Message);
            return BadRequest(new ApiError("The passkey could not be verified. Please start sign-in again."));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return BadRequest(new ApiError("The browser returned an invalid passkey response. Please try again."));
        }
    }
}
