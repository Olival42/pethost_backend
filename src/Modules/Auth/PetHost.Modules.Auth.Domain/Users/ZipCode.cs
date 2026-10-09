using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// CEP guardado <b>só com dígitos</b> (<c>87020000</c>). Aceita a entrada com máscara
/// (<c>87020-000</c>). Não confere se o CEP existe — isso exigiria consultar os
/// Correios; o front pode usar o ViaCEP para preencher o resto do endereço.
/// </summary>
public sealed class ZipCode : ValueObject
{
    public const int Length = 8;

    private ZipCode(string value) => Value = value;

    public string Value { get; }

    public static Result<ZipCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<ZipCode>.Failure(AuthErrors.ZipCodeInvalid);

        var trimmed = value.Trim();

        if (trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or '.')))
            return Result<ZipCode>.Failure(AuthErrors.ZipCodeInvalid);

        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        return digits.Length == Length && digits != "00000000"
            ? Result<ZipCode>.Success(new ZipCode(digits))
            : Result<ZipCode>.Failure(AuthErrors.ZipCodeInvalid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
