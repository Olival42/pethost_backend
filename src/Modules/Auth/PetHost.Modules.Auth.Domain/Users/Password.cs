using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Senha nova, em texto, que já passou na regra de senha forte. Só existe em memória,
/// a caminho do hash — o agregado guarda <see cref="PasswordHash"/>, nunca isto.
/// </summary>
/// <remarks>
/// Regra: 8 a 128 caracteres, com letra maiúscula, letra minúscula, número e caractere
/// especial. Vale para senha escolhida pelo usuário (hoje, a troca de senha). Não vale
/// para o login — conferir uma senha existente não pode depender de uma regra que
/// talvez nem existisse quando ela foi criada — nem para o admin do seed.
/// </remarks>
public sealed class Password : ValueObject
{
    public const int MinLength = 8;

    /// <summary>
    /// Teto para não transformar o hash em vetor de negação de serviço: o custo do
    /// Argon2 cresce com o tamanho da entrada.
    /// </summary>
    public const int MaxLength = 128;

    private Password(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Valida sem aparar nem normalizar — espaço faz parte da senha. Devolve
    /// <b>todas</b> as regras que falharam, não só a primeira.
    /// </summary>
    public static Result<Password> Create(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Result<Password>.Failure(AuthErrors.PasswordRequired);

        var errors = new List<Error>();

        if (value.Length < MinLength)
            errors.Add(AuthErrors.PasswordTooShort);

        if (value.Length > MaxLength)
            errors.Add(AuthErrors.PasswordTooLong);

        if (!value.Any(char.IsUpper))
            errors.Add(AuthErrors.PasswordMissingUppercase);

        if (!value.Any(char.IsLower))
            errors.Add(AuthErrors.PasswordMissingLowercase);

        if (!value.Any(char.IsDigit))
            errors.Add(AuthErrors.PasswordMissingDigit);

        if (!value.Any(IsSpecialCharacter))
            errors.Add(AuthErrors.PasswordMissingSpecialCharacter);

        return errors.Count > 0
            ? Result<Password>.Failure(errors)
            : Result<Password>.Success(new Password(value));
    }

    private static bool IsSpecialCharacter(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Nunca expõe a senha em log ou mensagem de erro (§15).</summary>
    public override string ToString() => "***";
}
