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
        Error.Validation("email", "AUTH_EMAIL_REQUIRED", "Email is required.");

    public static readonly Error EmailInvalidFormat =
        Error.Validation("email", "AUTH_EMAIL_INVALID_FORMAT", "Email format is invalid.");

    public static readonly Error EmailTooLong =
        Error.Validation("email", "AUTH_EMAIL_TOO_LONG", $"Email must be at most {Email.MaxLength} characters.", ("max", Email.MaxLength));

    public static readonly Error FullNameRequired =
        Error.Validation("fullName", "AUTH_FULL_NAME_REQUIRED", "Full name is required.");

    public static readonly Error FullNameTooLong =
        Error.Validation("fullName", "AUTH_FULL_NAME_TOO_LONG", $"Full name must be at most {FullName.MaxLength} characters.", ("max", FullName.MaxLength));

    /// <summary>Cadastro: só owner e host.</summary>
    public static readonly Error RoleInvalid =
        Error.Validation("role", "AUTH_ROLE_INVALID", "Role must be either 'owner' or 'host'.");

    /// <summary>Login: o admin também entra, mas pela mesma rota.</summary>
    public static readonly Error RoleUnknown =
        Error.Validation("role", "AUTH_ROLE_UNKNOWN", "Role must be one of 'owner', 'host' or 'admin'.");

    public static readonly Error PasswordRequired =
        Error.Validation("password", "AUTH_PASSWORD_REQUIRED", "Password is required.");

    // Senha forte (Password). Cada regra é um erro: o cliente recebe a lista do que falta.

    public static readonly Error PasswordTooShort =
        Error.Validation("password", "AUTH_PASSWORD_TOO_SHORT", $"Password must be at least {Password.MinLength} characters.", ("min", Password.MinLength));

    public static readonly Error PasswordTooLong =
        Error.Validation("password", "AUTH_PASSWORD_TOO_LONG", $"Password must be at most {Password.MaxLength} characters.", ("max", Password.MaxLength));

    public static readonly Error PasswordMissingUppercase =
        Error.Validation("password", "AUTH_PASSWORD_MISSING_UPPERCASE", "Password must contain at least one uppercase letter.");

    public static readonly Error PasswordMissingLowercase =
        Error.Validation("password", "AUTH_PASSWORD_MISSING_LOWERCASE", "Password must contain at least one lowercase letter.");

    public static readonly Error PasswordMissingDigit =
        Error.Validation("password", "AUTH_PASSWORD_MISSING_DIGIT", "Password must contain at least one number.");

    public static readonly Error PasswordMissingSpecialCharacter =
        Error.Validation("password", "AUTH_PASSWORD_MISSING_SPECIAL_CHARACTER", "Password must contain at least one special character.");

    // --- Troca de senha logada ---

    public static readonly Error CurrentPasswordRequired =
        Error.Validation("currentPassword", "AUTH_CURRENT_PASSWORD_REQUIRED", "Current password is required.");

    /// <summary>
    /// 400 no campo, e não 401: o token é válido, quem errou foi a senha digitada. Um 401
    /// faria o app achar que a sessão caiu.
    /// </summary>
    public static readonly Error CurrentPasswordIncorrect =
        Error.Validation("currentPassword", "AUTH_CURRENT_PASSWORD_INCORRECT", "Current password is incorrect.");

    public static readonly Error NewPasswordSameAsCurrent =
        Error.Validation("newPassword", "AUTH_NEW_PASSWORD_SAME_AS_CURRENT", "New password must be different from the current password.");

    // --- Perfil ---

    public static readonly Error PhoneRequired =
        Error.Validation("phone", "AUTH_PHONE_REQUIRED", "Phone is required.");

    public static readonly Error PhoneInvalid =
        Error.Validation("phone", "AUTH_PHONE_INVALID", $"Phone must have area code and number: {PhoneNumber.MinDigits} to {PhoneNumber.MaxDigits} digits.", ("min", PhoneNumber.MinDigits), ("max", PhoneNumber.MaxDigits));

    public static readonly Error AvatarUrlInvalid =
        Error.Validation("avatarUrl", "AUTH_AVATAR_URL_INVALID", "Avatar URL must be an absolute http or https address.");

    public static readonly Error AvatarUrlTooLong =
        Error.Validation("avatarUrl", "AUTH_AVATAR_URL_TOO_LONG", $"Avatar URL must be at most {AvatarUrl.MaxLength} characters.", ("max", AvatarUrl.MaxLength));

    public static readonly Error StateInvalid =
        Error.Validation("state", "AUTH_STATE_INVALID", "State must be a valid Brazilian state code, e.g. 'PR'.");

    public static readonly Error BirthDateRequired =
        Error.Validation("birthDate", "AUTH_BIRTH_DATE_REQUIRED", "Birth date is required.");

    public static readonly Error BirthDateInvalidFormat =
        Error.Validation("birthDate", "AUTH_BIRTH_DATE_INVALID_FORMAT", "Birth date must be a valid date in the format yyyy-MM-dd.");

    public static readonly Error AddressRequired =
        Error.Validation("address", "AUTH_ADDRESS_REQUIRED", "Address is required.");

    public static readonly Error BirthDateInFuture =
        Error.Validation("birthDate", "AUTH_BIRTH_DATE_IN_FUTURE", "Birth date cannot be in the future.");

    public static readonly Error Underage =
        Error.Validation("birthDate", "AUTH_UNDERAGE", $"You must be at least {User.MinimumAge} years old.", ("min", User.MinimumAge));

    // --- Endereço (o campo leva o prefixo "address." porque o JSON é aninhado) ---

    public static readonly Error ZipCodeInvalid =
        Error.Validation("address.zipCode", "AUTH_ZIP_CODE_INVALID", $"Zip code (CEP) must have {ZipCode.Length} digits.", ("digits", ZipCode.Length));

    public static readonly Error StreetRequired =
        Error.Validation("address.street", "AUTH_STREET_REQUIRED", "Street is required.");

    public static readonly Error StreetTooLong =
        Error.Validation("address.street", "AUTH_STREET_TOO_LONG", $"Street must be at most {Address.StreetMaxLength} characters.", ("max", Address.StreetMaxLength));

    public static readonly Error StreetNumberRequired =
        Error.Validation("address.number", "AUTH_STREET_NUMBER_REQUIRED", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error StreetNumberTooLong =
        Error.Validation("address.number", "AUTH_STREET_NUMBER_TOO_LONG", $"Number must be at most {Address.NumberMaxLength} characters.", ("max", Address.NumberMaxLength));

    public static readonly Error ComplementTooLong =
        Error.Validation("address.complement", "AUTH_COMPLEMENT_TOO_LONG", $"Complement must be at most {Address.ComplementMaxLength} characters.", ("max", Address.ComplementMaxLength));

    public static readonly Error NeighborhoodRequired =
        Error.Validation("address.neighborhood", "AUTH_NEIGHBORHOOD_REQUIRED", "Neighborhood is required.");

    public static readonly Error NeighborhoodTooLong =
        Error.Validation("address.neighborhood", "AUTH_NEIGHBORHOOD_TOO_LONG", $"Neighborhood must be at most {Address.NeighborhoodMaxLength} characters.", ("max", Address.NeighborhoodMaxLength));

    public static readonly Error CityRequired =
        Error.Validation("address.city", "AUTH_CITY_REQUIRED", "City is required.");

    public static readonly Error CityTooLong =
        Error.Validation("address.city", "AUTH_CITY_TOO_LONG", $"City must be at most {Address.CityMaxLength} characters.", ("max", Address.CityMaxLength));

    public static readonly Error AddressStateInvalid =
        Error.Validation("address.state", "AUTH_ADDRESS_STATE_INVALID", "State must be a valid Brazilian state code, e.g. 'PR'.");

    public static readonly Error RefreshTokenRequired =
        Error.Validation("refreshToken", "AUTH_REFRESH_TOKEN_REQUIRED", "Refresh token is required.");

    // --- Credenciais e sessão (401) ---

    /// <summary>
    /// Mensagem deliberadamente genérica: não revela se o e-mail existe,
    /// para não virar oráculo de enumeração de contas.
    /// </summary>
    public static readonly Error InvalidCredentials =
        new("AUTH_INVALID_CREDENTIALS", "Email or password is incorrect.");

    public static readonly Error RefreshTokenInvalid =
        new("AUTH_REFRESH_TOKEN_INVALID", "The refresh token is invalid, expired or already used.");

    /// <summary>
    /// Token de troca de senha inexistente, vencido, já usado ou substituído por um
    /// pedido mais novo. Os quatro casos têm a mesma resposta.
    /// </summary>
    public static readonly Error PasswordResetTokenInvalid =
        new("AUTH_PASSWORD_RESET_TOKEN_INVALID", "The password reset token is invalid, expired or already used.");

    // --- Regras de negócio ---

    /// <summary>409: já existe conta com esse e-mail nesse papel.</summary>
    public static readonly Error EmailAlreadyRegistered =
        new("AUTH_EMAIL_ALREADY_REGISTERED", "An account with this email already exists for this role.");

    /// <summary>403: o admin é criado pelo seed, nunca pela API (dicionário, tabela users).</summary>
    public static readonly Error AdminRegistrationForbidden =
        new("AUTH_ADMIN_REGISTRATION_FORBIDDEN", "Admin accounts cannot be created through the API.");

    /// <summary>403: login, refresh ou troca de senha numa conta inativada.</summary>
    public static readonly Error AccountDeactivated =
        new("AUTH_ACCOUNT_DEACTIVATED", "This account is deactivated. Reactivate it to sign in again.");

    /// <summary>
    /// 403: conta suspensa pelo admin. Aparece só depois da senha conferida, como a
    /// inativa — e, ao contrário dela, não se resolve reativando.
    /// </summary>
    public static readonly Error AccountSuspended =
        new("AUTH_ACCOUNT_SUSPENDED", "This account is suspended. Contact support.");

    /// <summary>403: o admin não pode ser suspenso.</summary>
    public static readonly Error AdminSuspensionForbidden =
        new("AUTH_ADMIN_SUSPENSION_FORBIDDEN", "The admin account cannot be suspended.");

    public static readonly Error SuspensionReasonRequired =
        Error.Validation("reason", "AUTH_SUSPENSION_REASON_REQUIRED", "A reason is required to suspend an account.");

    public static readonly Error SuspensionReasonTooLong =
        Error.Validation("reason", "AUTH_SUSPENSION_REASON_TOO_LONG", $"Reason must be at most {User.SuspensionReasonMaxLength} characters.", ("max", User.SuspensionReasonMaxLength));

    /// <summary>403: o admin não pode ser inativado.</summary>
    public static readonly Error AdminDeactivationForbidden =
        new("AUTH_ADMIN_DEACTIVATION_FORBIDDEN", "The admin account cannot be deactivated.");

    /// <summary>403: o admin não pode ser excluído (o seed não o recriaria).</summary>
    public static readonly Error AdminErasureForbidden =
        new("AUTH_ADMIN_ERASURE_FORBIDDEN", "The admin account cannot be deleted.");

    /// <summary>422: a conta foi excluída a pedido do titular e não volta.</summary>
    public static readonly Error AccountErased =
        new("AUTH_ACCOUNT_ERASED", "This account was deleted and cannot be reactivated.");

    /// <summary>
    /// 404 na troca de conta: não existe conta ativa com o mesmo e-mail no papel pedido,
    /// ou a senha não confere. Mesma resposta nos dois casos.
    /// </summary>
    public static readonly Error LinkedAccountNotFound =
        new("AUTH_LINKED_ACCOUNT_NOT_FOUND", "No active account with this email was found for the requested role, or the password is incorrect.");

    // --- Invariantes internas (indicam bug, não entrada do usuário) ---

    public static readonly Error PasswordHashRequired =
        new("AUTH_PASSWORD_HASH_INVALID", "The password hash cannot be empty.");

    public static readonly Error PasswordHashTooLong =
        new("AUTH_PASSWORD_HASH_INVALID", $"The password hash exceeds {PasswordHash.MaxLength} characters.");

    public static Error UserNotFound(UserId id) =>
        new("AUTH_USER_NOT_FOUND", $"User '{id.Value}' was not found.");

    // --- Validação: identificadores e campos obrigatórios de comandos internos e de rota ---

    public static readonly Error RoleRequired =
        Error.Validation("role", "AUTH_ROLE_REQUIRED", "Role is required.");

    public static readonly Error ResetTokenRequired =
        Error.Validation("token", "AUTH_RESET_TOKEN_REQUIRED", "Token is required.");
}
