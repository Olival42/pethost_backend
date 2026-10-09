namespace PetHost.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Nomes das políticas de rate limit, usados em <c>[EnableRateLimiting(...)]</c> nos
/// controllers. Além delas, todo endpoint passa pelo limite geral.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Senha conferida sem sessão: login, troca de conta e reativação. Por IP.</summary>
    public const string Credentials = "credentials";

    /// <summary>Criação de conta. Por IP.</summary>
    public const string Registration = "registration";

    /// <summary>Esqueci a senha (pedido e troca com o token do e-mail). Por IP.</summary>
    public const string PasswordReset = "password-reset";

    /// <summary>Refresh e logout. Por IP.</summary>
    public const string Session = "session";

    /// <summary>Ação sensível da conta logada: trocar a senha, inativar. Por usuário.</summary>
    public const string AccountSensitive = "account-sensitive";

    /// <summary>Alteração de perfil (PATCH). Por usuário.</summary>
    public const string AccountUpdate = "account-update";

    public static readonly IReadOnlyList<string> All =
        [Credentials, Registration, PasswordReset, Session, AccountSensitive, AccountUpdate];
}
