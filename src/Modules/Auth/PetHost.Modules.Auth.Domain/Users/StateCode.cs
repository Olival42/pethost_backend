using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>UF brasileira em duas letras maiúsculas, ex.: <c>PR</c>. Só as 27 que existem.</summary>
public sealed class StateCode : ValueObject
{
    /// <summary>Tamanho de <c>users.state</c> (<c>char(2)</c>) no dicionário de dados.</summary>
    public const int Length = 2;

    private static readonly HashSet<string> ValidCodes =
    [
        "AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA",
        "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO",
    ];

    private StateCode(string value) => Value = value;

    public string Value { get; }

    public static Result<StateCode> Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();

        return normalized is not null && ValidCodes.Contains(normalized)
            ? Result<StateCode>.Success(new StateCode(normalized))
            : Result<StateCode>.Failure(AuthErrors.StateInvalid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
