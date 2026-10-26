namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Configuração do "esqueci a senha". Vem do ambiente como
/// <c>PasswordReset__TokenLifetimeMinutes</c> e <c>PasswordReset__ResetUrl</c>.
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Marcador trocado pelo token em <see cref="ResetUrl"/>.</summary>
    public const string TokenPlaceholder = "{token}";

    /// <summary>Validade do token, em minutos. Também é o TTL da chave no Redis.</summary>
    public int TokenLifetimeMinutes { get; init; } = 30;

    /// <summary>
    /// Opcional. Endereço da tela de nova senha no front, com <c>{token}</c> onde o
    /// token entra — ex.: <c>https://app.pethost.com/reset-password#token={token}</c>.
    /// Vazio usa Email:AppUrl com /reset-password#token=; sem ambos, traz só o código para copiar.
    /// </summary>
    public string? ResetUrl { get; init; }
}
