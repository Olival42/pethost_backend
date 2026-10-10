using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// Uma foto do pet (tabela <c>pet.pet_photos</c>). Faz parte do agregado <see cref="Pet"/>:
/// só entra, muda e sai por <see cref="Pet.AddPhoto"/>, <see cref="Pet.ReplacePhoto"/> e
/// <see cref="Pet.RemovePhoto"/>.
/// </summary>
/// <remarks>
/// A URL vem do bucket de imagens, nunca do cliente. A <see cref="Position"/> é a vaga da
/// foto (1 a <see cref="Pet.MaxPhotos"/>): a menor é a capa, e uma vaga liberada é a
/// primeira a ser ocupada de novo.
/// </remarks>
public sealed class PetPhoto
{
    public const int UrlMaxLength = 500;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private PetPhoto()
    {
        Url = string.Empty;
    }

    private PetPhoto(PetId petId, string url, string contentHash, int position, DateTimeOffset now)
    {
        Id = PetPhotoId.New();
        PetId = petId;
        Url = url;
        ContentHash = contentHash;
        Position = position;
        CreatedAt = now;
    }

    public PetPhotoId Id { get; private set; }

    public PetId PetId { get; private set; }

    /// <summary>URL pública absoluta da imagem no bucket.</summary>
    public string Url { get; private set; }

    /// <summary>
    /// SHA-256 da imagem (64 caracteres hexadecimais): recusa a mesma imagem duas vezes no pet.
    /// Nulo só nas fotos de antes desta regra.
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>Vaga da foto, de 1 a <see cref="Pet.MaxPhotos"/>. A menor é a capa.</summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Result<PetPhoto> Create(PetId petId, string? url, string contentHash, int position, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var checkedUrl = CheckUrl(url);

        return checkedUrl.IsFailure
            ? Result<PetPhoto>.FromFailure(checkedUrl)
            : Result<PetPhoto>.Success(new PetPhoto(petId, checkedUrl.Value!, contentHash, position, now));
    }

    /// <summary>Troca a imagem e mantém o id e a vaga. Devolve a URL anterior.</summary>
    internal Result<string> ChangeImage(string? url, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var checkedUrl = CheckUrl(url);
        if (checkedUrl.IsFailure)
            return checkedUrl;

        var previous = Url;
        Url = checkedUrl.Value!;
        ContentHash = contentHash;

        return Result<string>.Success(previous);
    }

    private static Result<string> CheckUrl(string? url)
    {
        var text = url?.Trim();

        if (string.IsNullOrEmpty(text))
            return Result<string>.Failure(PetsErrors.PhotoUrlInvalid);

        if (text.Length > UrlMaxLength)
            return Result<string>.Failure(PetsErrors.PhotoUrlTooLong);

        // Recusa javascript:, data: e caminho relativo: o front usa isto como src de imagem.
        var isWebAddress = Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return isWebAddress
            ? Result<string>.Success(text)
            : Result<string>.Failure(PetsErrors.PhotoUrlInvalid);
    }
}

