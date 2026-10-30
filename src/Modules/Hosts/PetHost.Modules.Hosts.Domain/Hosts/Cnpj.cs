using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// CNPJ guardado sem máscara, em maiúsculas (<c>12ABC34501DE35</c>). Aceita a entrada com
/// máscara (<c>12.ABC.345/01DE-35</c>) e confere os dois dígitos verificadores.
/// </summary>
/// <remarks>
/// Desde julho de 2026 a Receita emite CNPJ <b>alfanumérico</b>: as 12 primeiras posições
/// podem ter letras; os 2 dígitos verificadores continuam numéricos. O cálculo é o mesmo
/// módulo 11, com o valor de cada caractere = código ASCII − 48 (dígito vale ele mesmo,
/// <c>A</c> = 17, <c>B</c> = 18...). CNPJ antigo, só com dígitos, continua válido.
/// </remarks>
public sealed class Cnpj : ValueObject
{
    public const int Length = 14;

    private static readonly int[] FirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Cnpj(string value) => Value = value;

    public string Value { get; }

    public static Result<Cnpj> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<Cnpj>.Failure(HostsErrors.CnpjInvalid);

        var trimmed = value.Trim().ToUpperInvariant();

        // Letras, dígitos e a máscara usual. Outro símbolo é erro, não ruído.
        if (trimmed.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '/' or '-')))
            return Result<Cnpj>.Failure(HostsErrors.CnpjInvalid);

        var characters = new string([.. trimmed.Where(char.IsAsciiLetterOrDigit)]);

        return IsValid(characters)
            ? Result<Cnpj>.Success(new Cnpj(characters))
            : Result<Cnpj>.Failure(HostsErrors.CnpjInvalid);
    }

    private static bool IsValid(string characters)
    {
        if (characters.Length != Length || characters.All(c => c == characters[0]))
            return false;

        // Verificadores sempre numéricos.
        if (!char.IsAsciiDigit(characters[12]) || !char.IsAsciiDigit(characters[13]))
            return false;

        return CheckDigit(characters, FirstWeights) == characters[12] - '0'
            && CheckDigit(characters, SecondWeights) == characters[13] - '0';
    }

    private static int CheckDigit(string characters, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
            sum += (characters[i] - '0') * weights[i];

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
