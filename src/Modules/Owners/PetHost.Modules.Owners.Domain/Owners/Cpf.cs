using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Domain.Owners;

/// <summary>
/// CPF guardado <b>só com dígitos</b> (<c>52998224725</c>). Aceita a entrada com
/// máscara (<c>529.982.247-25</c>) e confere os dois dígitos verificadores.
/// </summary>
/// <remarks>
/// Dado pessoal sensível para a LGPD: <see cref="ToString"/> mascara, para o CPF
/// não vazar em log por descuido. Use <see cref="Value"/> só onde ele é necessário
/// (banco, Stripe).
/// </remarks>
public sealed class Cpf : ValueObject
{
    public const int Length = 11;

    private Cpf(string value) => Value = value;

    public string Value { get; }

    public static Result<Cpf> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<Cpf>.Failure(OwnersErrors.CpfInvalid);

        var trimmed = value.Trim();

        // Só dígitos e a máscara usual. Letra é erro, não ruído.
        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-')))
            return Result<Cpf>.Failure(OwnersErrors.CpfInvalid);

        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return HasValidCheckDigits(digits)
            ? Result<Cpf>.Success(new Cpf(digits))
            : Result<Cpf>.Failure(OwnersErrors.CpfInvalid);
    }

    /// <summary>
    /// Módulo 11 da Receita Federal. CPFs com todos os dígitos iguais
    /// (<c>111.111.111-11</c>) passam na conta, mas não existem.
    /// </summary>
    private static bool HasValidCheckDigits(string digits)
    {
        if (digits.Length != Length || digits.All(c => c == digits[0]))
            return false;

        return CheckDigit(digits, 9) == digits[9] - '0'
            && CheckDigit(digits, 10) == digits[10] - '0';
    }

    private static int CheckDigit(string digits, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += (digits[i] - '0') * (count + 1 - i);

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Mascarado: <c>***.***.247-25</c>.</summary>
    public override string ToString() => $"***.***.{Value[6..9]}-{Value[9..]}";
}
