namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Configuração do JWT. Vem do ambiente como <c>Jwt__Issuer</c>, <c>Jwt__Audience</c>,
/// <c>Jwt__Key</c>, <c>Jwt__AccessTokenLifetimeMinutes</c> e
/// <c>Jwt__RefreshTokenLifetimeDays</c> (ver <c>.env.example</c>).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Tamanho mínimo da chave para HS256: 256 bits.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>Quem emitiu o token. Vai na claim <c>iss</c> e é validado na entrada.</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Para quem o token vale. Vai na claim <c>aud</c> e é validado na entrada.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Segredo da assinatura HMAC-SHA256. Nunca versionado.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Vida do access token, em minutos.</summary>
    public long AccessTokenLifetimeMinutes { get; init; }

    /// <summary>Vida do refresh token, em dias. Também é o TTL da chave no Redis.</summary>
    public long RefreshTokenLifetimeDays { get; init; }
}
