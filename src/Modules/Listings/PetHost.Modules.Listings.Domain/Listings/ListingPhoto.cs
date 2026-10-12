using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Uma foto do cantinho (tabela <c>listing.listing_photos</c>). Faz parte do agregado
/// <see cref="Listing"/>: só entra, muda e sai por <see cref="Listing.AddPhoto"/>,
/// <see cref="Listing.ReplacePhoto"/> e <see cref="Listing.RemovePhoto"/>.
/// </summary>
/// <remarks>
/// A URL vem do bucket de imagens, nunca do cliente. A <see cref="Position"/> é a vaga da foto
/// (1 a <see cref="Listing.MaxPhotos"/>): a menor é a capa, e uma vaga liberada é a primeira a
/// ser ocupada de novo.
/// </remarks>
public sealed class ListingPhoto
{
    public const int UrlMaxLength = 500;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private ListingPhoto()
    {
        Url = string.Empty;
        ContentHash = string.Empty;
    }

    private ListingPhoto(ListingId listingId, string url, string contentHash, int position, DateTimeOffset now)
    {
        Id = ListingPhotoId.New();
        ListingId = listingId;
        Url = url;
        ContentHash = contentHash;
        Position = position;
        CreatedAt = now;
    }

    public ListingPhotoId Id { get; private set; }

    public ListingId ListingId { get; private set; }

    /// <summary>URL pública absoluta da imagem no bucket.</summary>
    public string Url { get; private set; }

    /// <summary>SHA-256 da imagem (64 caracteres hexadecimais): a mesma imagem não entra duas vezes no cantinho.</summary>
    public string ContentHash { get; private set; }

    /// <summary>Vaga da foto, de 1 a <see cref="Listing.MaxPhotos"/>. A menor é a capa.</summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Result<ListingPhoto> Create(ListingId listingId, string? url, string contentHash, int position, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var checkedUrl = CheckUrl(url);

        return checkedUrl.IsFailure
            ? Result<ListingPhoto>.FromFailure(checkedUrl)
            : Result<ListingPhoto>.Success(new ListingPhoto(listingId, checkedUrl.Value!, contentHash, position, now));
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

    /// <summary>Muda a posição na fila. Só o agregado chama, sempre renumerando todas juntas.</summary>
    internal void MoveTo(int position) => Position = position;

    private static Result<string> CheckUrl(string? url)
    {
        var text = url?.Trim();

        if (string.IsNullOrEmpty(text))
            return Result<string>.Failure(ListingsErrors.PhotoUrlInvalid);

        if (text.Length > UrlMaxLength)
            return Result<string>.Failure(ListingsErrors.PhotoUrlTooLong);

        // Recusa javascript:, data: e caminho relativo: o front usa isto como src de imagem.
        var isWebAddress = Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return isWebAddress
            ? Result<string>.Success(text)
            : Result<string>.Failure(ListingsErrors.PhotoUrlInvalid);
    }
}
