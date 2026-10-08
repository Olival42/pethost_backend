using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Hash da senha. Existe para que o agregado seja incapaz de guardar senha em texto:
/// o tipo só é construído a partir de um hash já calculado (Argon2id na Infrastructure).
/// </summary>
public sealed class PasswordHash : ValueObject
{
    /// <summary>Limite de <c>users.password_hash</c> no dicionário de dados.</summary>
    public const int MaxLength = 255;

    private PasswordHash(string value) => Value = value;

    public string Value { get; }

    /// <summary>Envolve um hash já calculado. Nunca recebe senha em texto.</summary>
    public static Result<PasswordHash> FromHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Result<PasswordHash>.Failure(AuthErrors.PasswordHashRequired);

        return hash.Length > MaxLength
            ? Result<PasswordHash>.Failure(AuthErrors.PasswordHashTooLong)
            : Result<PasswordHash>.Success(new PasswordHash(hash));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Nunca expõe o hash em log ou mensagem de erro (§15).</summary>
    public override string ToString() => "***";
}
