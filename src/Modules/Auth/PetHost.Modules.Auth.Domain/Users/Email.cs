using System.Text.RegularExpressions;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// E-mail de login, normalizado. Única no banco junto com a role, não isolada —
/// o mesmo e-mail pode existir uma vez como owner e uma como host (dicionário, UK¹).
/// </summary>
public sealed partial class Email : ValueObject
{
    /// <summary>Limite de <c>users.email</c> no dicionário de dados.</summary>
    public const int MaxLength = 160;

    private Email(string value) => Value = value;

    public string Value { get; }

    /// <summary>Normaliza (apara e baixa a caixa) e valida o formato.</summary>
    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<Email>.Failure(AuthErrors.EmailRequired);

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
            return Result<Email>.Failure(AuthErrors.EmailTooLong);

        return EmailPattern().IsMatch(normalized)
            ? Result<Email>.Success(new Email(normalized))
            : Result<Email>.Failure(AuthErrors.EmailInvalidFormat);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
