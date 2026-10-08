namespace PetHost.Modules.Auth.Application.Abstractions;

/// <param name="Value">O JWT assinado.</param>
/// <param name="ExpiresAt">Instante da expiração, em UTC.</param>
/// <param name="ExpiresInSeconds">Segundos até expirar, contados na emissão.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, long ExpiresInSeconds);
