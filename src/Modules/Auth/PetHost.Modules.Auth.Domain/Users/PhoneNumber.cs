using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Telefone brasileiro com DDD, guardado <b>só com dígitos</b>: <c>44999990000</c>.
/// Aceita a entrada formatada (<c>(44) 99999-0000</c>, <c>+55 44 ...</c>); a máscara
/// de exibição é trabalho do front.
/// </summary>
public sealed class PhoneNumber : ValueObject
{
    /// <summary>Limite de <c>users.phone</c> no dicionário de dados.</summary>
    public const int MaxLength = 20;

    /// <summary>DDD + fixo de 8 dígitos.</summary>
    public const int MinDigits = 10;

    /// <summary>55 + DDD + celular de 9 dígitos.</summary>
    public const int MaxDigits = 13;

    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<PhoneNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<PhoneNumber>.Failure(AuthErrors.PhoneInvalid);

        var trimmed = value.Trim();

        // Só dígitos e a pontuação usual de telefone. Letra é erro, não ruído.
        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '-' or '(' or ')' or '+' or '.')))
            return Result<PhoneNumber>.Failure(AuthErrors.PhoneInvalid);

        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return digits.Length is < MinDigits or > MaxDigits
            ? Result<PhoneNumber>.Failure(AuthErrors.PhoneInvalid)
            : Result<PhoneNumber>.Success(new PhoneNumber(digits));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
