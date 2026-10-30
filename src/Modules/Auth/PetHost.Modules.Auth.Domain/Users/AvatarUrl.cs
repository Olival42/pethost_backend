using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Endereço da foto de perfil: absoluto e <c>http</c>/<c>https</c>. Recusa
/// <c>javascript:</c>, <c>data:</c> e caminhos relativos, que o front usaria como
/// <c>src</c> de imagem.
/// </summary>
public sealed class AvatarUrl : ValueObject
{
    /// <summary>Limite de <c>users.avatar_url</c> no dicionário de dados.</summary>
    public const int MaxLength = 500;

    private AvatarUrl(string value) => Value = value;

    public string Value { get; }

    public static Result<AvatarUrl> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<AvatarUrl>.Failure(AuthErrors.AvatarUrlInvalid);

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            return Result<AvatarUrl>.Failure(AuthErrors.AvatarUrlTooLong);

        var isWebAddress = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return isWebAddress
            ? Result<AvatarUrl>.Success(new AvatarUrl(trimmed))
            : Result<AvatarUrl>.Failure(AuthErrors.AvatarUrlInvalid);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
