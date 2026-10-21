using System.ComponentModel.DataAnnotations;
using Fido2NetLib;

namespace PasskeyDemo.Api;

public sealed class WebAuthnSettings
{
    public string FrontendOrigin { get; set; } = "https://localhost:4200";
    public string RpId { get; set; } = "localhost";
    public string RpName { get; set; } = "Passkey Studio";
    public int ChallengeLifetimeSeconds { get; set; } = 120;
}

public sealed record PasswordLoginRequest(
    [Required, StringLength(100)] string Username,
    [Required, StringLength(200)] string Password);
public sealed record RegistrationStartRequest(
    [Required, StringLength(60, MinimumLength = 1)] string Name);
public sealed record RegistrationFinishRequest(
    [Required] string CeremonyId,
    [Required] AuthenticatorAttestationRawResponse Credential);
public sealed record AuthenticationFinishRequest(
    [Required] string CeremonyId,
    [Required] AuthenticatorAssertionRawResponse Credential);
public sealed record UserView(string Username, string DisplayName, string LoginMethod);
public sealed record PasskeyView(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
public sealed record ApiError(string Message);
