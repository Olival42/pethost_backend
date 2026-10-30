using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// CPF guardado <b>só com dígitos</b>. Mesma regra do CPF do tutor (módulo Owners): os
/// módulos não se referenciam (§5), então a regra é repetida aqui.
/// </summary>
/// <remarks>Dado pessoal sensível (LGPD): <see cref="ToString"/> mascara.</remarks>
public sealed class Cpf : ValueObject
{
    public const int Length = 11;

    private Cpf(string value) => Value = value;

    public string Value { get; }

    public static Result<Cpf> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<Cpf>.Failure(HostsErrors.CpfInvalid);

        var trimmed = value.Trim();

        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-')))
            return Result<Cpf>.Failure(HostsErrors.CpfInvalid);

        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return HasValidCheckDigits(digits)
            ? Result<Cpf>.Success(new Cpf(digits))
            : Result<Cpf>.Failure(HostsErrors.CpfInvalid);
    }

    /// <summary>Módulo 11 da Receita. Todos os dígitos iguais passam na conta, mas não existem.</summary>
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
