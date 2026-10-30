using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>Nome completo do usuário, aparado. Obrigatório em toda conta.</summary>
public sealed class FullName : ValueObject
{
    /// <summary>Limite de <c>users.full_name</c> no dicionário de dados.</summary>
    public const int MaxLength = 120;

    private FullName(string value) => Value = value;

    public string Value { get; }

    public static Result<FullName> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<FullName>.Failure(AuthErrors.FullNameRequired);

        var normalized = value.Trim();

        return normalized.Length > MaxLength
            ? Result<FullName>.Failure(AuthErrors.FullNameTooLong)
            : Result<FullName>.Success(new FullName(normalized));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
