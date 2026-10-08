namespace PetHost.Modules.Auth.Application.Abstractions;

/// <param name="Value">Valor opaco entregue ao cliente. Não é um JWT.</param>
/// <param name="ExpiresAt">Instante da expiração, em UTC.</param>
public sealed record RefreshToken(string Value, DateTimeOffset ExpiresAt);
