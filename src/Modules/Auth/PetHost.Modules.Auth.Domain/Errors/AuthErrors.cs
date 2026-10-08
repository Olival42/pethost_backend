using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Auth.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Auth. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class AuthErrors
{
    // --- Validação de formato (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error EmailRequired =
        Error.Validation("email", "Email is required.");

    public static readonly Error EmailInvalidFormat =
        Error.Validation("email", "Email format is invalid.");

    public static readonly Error EmailTooLong =
        Error.Validation("email", $"Email must be at most {Email.MaxLength} characters.");

    public static readonly Error FullNameRequired =
        Error.Validation("fullName", "Full name is required.");

    public static readonly Error FullNameTooLong =
        Error.Validation("fullName", $"Full name must be at most {User.FullNameMaxLength} characters.");

    /// <summary>Cadastro: só owner e host.</summary>
    public static readonly Error RoleInvalid =
        Error.Validation("role", "Role must be either 'owner' or 'host'.");

    /// <summary>Login: o admin também entra, mas pela mesma rota.</summary>
    public static readonly Error RoleUnknown =
        Error.Validation("role", "Role must be one of 'owner', 'host' or 'admin'.");

    public static readonly Error PasswordRequired =
        Error.Validation("password", "Password is required.");

    public static readonly Error RefreshTokenRequired =
        Error.Validation("refreshToken", "Refresh token is required.");

    // --- Credenciais e sessão (401) ---

    /// <summary>
    /// Mensagem deliberadamente genérica: não revela se o e-mail existe,
    /// para não virar oráculo de enumeração de contas.
    /// </summary>
    public static readonly Error InvalidCredentials =
        new("AUTH_INVALID_CREDENTIALS", "Email or password is incorrect.");

    public static readonly Error RefreshTokenInvalid =
        new("AUTH_REFRESH_TOKEN_INVALID", "The refresh token is invalid, expired or already used.");

    // --- Regras de negócio ---

    /// <summary>403: o admin é criado pelo seed, nunca pela API (dicionário, tabela users).</summary>
    public static readonly Error AdminRegistrationForbidden =
        new("AUTH_ADMIN_REGISTRATION_FORBIDDEN", "Admin accounts cannot be created through the API.");

    // --- Invariantes internas (indicam bug, não entrada do usuário) ---

    public static readonly Error PasswordHashRequired =
        new("AUTH_PASSWORD_HASH_INVALID", "The password hash cannot be empty.");

    public static readonly Error PasswordHashTooLong =
        new("AUTH_PASSWORD_HASH_INVALID", $"The password hash exceeds {PasswordHash.MaxLength} characters.");

    public static Error UserNotFound(UserId id) =>
        new("AUTH_USER_NOT_FOUND", $"User '{id.Value}' was not found.");
}
