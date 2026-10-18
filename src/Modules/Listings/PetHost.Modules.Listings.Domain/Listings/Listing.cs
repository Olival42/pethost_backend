using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// O cantinho: o espaço que o anfitrião oferece — a descrição (<see cref="Details"/>), o
/// endereço (<see cref="Address"/>) com a coordenada (<see cref="Location"/>) e as fotos.
/// Espelha as tabelas <c>listing.listings</c> e <c>listing.listing_photos</c>.
/// </summary>
/// <remarks>
/// Guarda o id do anfitrião (<c>host.hosts.id</c>, FK lógica), não o da conta. Pessoa física tem
/// no máximo <see cref="MaxListingsPerIndividual"/> cantinho; empresa pode ter vários, um por
/// filial (<see cref="Create"/>).
/// <para>
/// Cantinho não é apagado: é desativado (<see cref="Deactivate"/>), some para os outros e pode
/// voltar. Inativo só o dono vê.
/// </para>
/// </remarks>
public sealed class Listing : Entity<ListingId>
{
    /// <summary>Quantas fotos um cantinho pode ter.</summary>
    public const int MaxPhotos = 5;

    /// <summary>Quantos cantinhos uma pessoa física pode ter (inativos contam: reative em vez de criar outro).</summary>
    public const int MaxListingsPerIndividual = 1;

    private readonly List<ListingPhoto> _photos = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Listing()
    {
        Details = null!;
        Address = null!;
        Location = null!;
        ApproximateLocation = null!;
    }

    private Listing(Guid hostId, ListingDetails details, ListingAddress address, GeoPoint location, DateTimeOffset now)
        : base(ListingId.New())
    {
        HostId = hostId;
        Details = details;
        Address = address;
        Location = location;
        ApproximateLocation = location.Approximate(Random.Shared);
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Anfitrião dono (<c>host.hosts.id</c>).</summary>
    public Guid HostId { get; private set; }

    public ListingDetails Details { get; private set; }

    public ListingAddress Address { get; private set; }

    /// <summary>Coordenada do endereço, vinda do geocodificador. Anda junto com <see cref="Address"/>.</summary>
    public GeoPoint Location { get; private set; }

    /// <summary>
    /// Ponto aproximado para o mapa de quem não é o dono (<see cref="GeoPoint.Approximate"/>).
    /// Sorteado uma vez e guardado; um novo sorteio só quando a <see cref="Location"/> muda.
    /// </summary>
    public GeoPoint ApproximateLocation { get; private set; }

    /// <summary>Até <see cref="MaxPhotos"/> fotos, pela vaga: a primeira é a capa.</summary>
    public IReadOnlyList<ListingPhoto> Photos => [.. _photos.OrderBy(photo => photo.Position)];

    /// <summary>Ainda cabe foto? Conferido antes de enviar a imagem ao bucket.</summary>
    public bool CanAddPhoto => _photos.Count < MaxPhotos;

    /// <summary>Falso depois de desativado. Nasce ativo.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Quando foi desativado pela última vez. Nulo enquanto ativo.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Cadastra o cantinho de <paramref name="host"/>. Pessoa física que já tem
    /// <see cref="MaxListingsPerIndividual"/> (ativo ou não): <c>LISTING_LIMIT_REACHED</c>.
    /// </summary>
    /// <param name="listingsOfHost">Quantos cantinhos o anfitrião já tem, ativos e inativos.</param>
    public static Result<Listing> Create(
        ListingHost host,
        int listingsOfHost,
        ListingDetails details,
        ListingAddress address,
        GeoPoint location,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(location);

        if (host.Id == Guid.Empty)
            throw new ArgumentException("The listing must belong to a host.", nameof(host));

        var allowed = EnsureCanCreate(host, listingsOfHost);
        if (allowed.IsFailure)
            return Result<Listing>.FromFailure(allowed);

        return Result<Listing>.Success(new Listing(host.Id, details, address, location, now));
    }

    /// <summary>
    /// O anfitrião pode ter mais um cantinho? Pessoa física só tem
    /// <see cref="MaxListingsPerIndividual"/>; empresa, quantos quiser (um por filial). Serve para
    /// conferir antes de gastar uma consulta ao mapa.
    /// </summary>
    public static Result EnsureCanCreate(ListingHost host, int listingsOfHost) =>
        !host.IsCompany && listingsOfHost >= MaxListingsPerIndividual
            ? Result.Failure(ListingsErrors.LimitReached)
            : Result.Success();

    /// <summary>O cantinho é do anfitrião <paramref name="hostId"/>?</summary>
    public bool IsOwnedBy(Guid hostId) => HostId == hostId;

    /// <summary>
    /// Troca a descrição. Igual à atual não é alteração: <see cref="UpdatedAt"/> só anda se algo
    /// mudou. Devolve se mudou. Vale também para cantinho desativado.
    /// </summary>
    public bool UpdateDetails(ListingDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (details == Details)
            return false;

        Details = details;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Troca o endereço e a coordenada juntos (a coordenada sai do endereço). Igual ao atual não
    /// é alteração. Com coordenada nova, sorteia outro <see cref="ApproximateLocation"/>. Devolve se mudou.
    /// </summary>
    public bool Relocate(ListingAddress address, GeoPoint location, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(location);

        if (address == Address && location == Location)
            return false;

        if (location != Location)
            ApproximateLocation = location.Approximate(Random.Shared);

        Address = address;
        Location = location;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Põe uma foto no fim da fila (a próxima posição). Sem vaga: <c>LISTING_PHOTO_LIMIT_REACHED</c>;
    /// imagem que o cantinho já tem: <c>LISTING_PHOTO_ALREADY_EXISTS</c>.
    /// </summary>
    public Result<ListingPhoto> AddPhoto(string url, string contentHash, DateTimeOffset now)
    {
        if (!CanAddPhoto)
            return Result<ListingPhoto>.Failure(ListingsErrors.PhotoLimitReached);

        if (HasImage(contentHash))
            return Result<ListingPhoto>.Failure(ListingsErrors.PhotoAlreadyExists);

        // As posições são sempre 1..n sem buraco (remover e reordenar compactam).
        var photo = ListingPhoto.Create(Id, url, contentHash, _photos.Count + 1, now);
        if (photo.IsFailure)
            return photo;

        _photos.Add(photo.Value!);
        UpdatedAt = now;

        return photo;
    }

    /// <summary>O cantinho já tem esta imagem (mesmo hash) em alguma foto?</summary>
    public bool HasImage(string contentHash) =>
        _photos.Exists(photo => string.Equals(photo.ContentHash, contentHash, StringComparison.Ordinal));

    /// <summary>A foto <paramref name="photoId"/> é deste cantinho?</summary>
    public bool HasPhoto(ListingPhotoId photoId) => _photos.Exists(photo => photo.Id == photoId);

    /// <summary>
    /// Troca a imagem da foto <paramref name="photoId"/>, mantendo o id e a vaga. Devolve a URL
    /// anterior (para apagar do bucket).
    /// </summary>
    public Result<string> ReplacePhoto(ListingPhotoId photoId, string url, string contentHash, DateTimeOffset now)
    {
        var photo = _photos.Find(candidate => candidate.Id == photoId);
        if (photo is null)
            return Result<string>.Failure(ListingsErrors.PhotoNotFound(photoId));

        if (HasImage(contentHash))
            return Result<string>.Failure(ListingsErrors.PhotoAlreadyExists);

        var previous = photo.ChangeImage(url, contentHash);
        if (previous.IsSuccess)
            UpdatedAt = now;

        return previous;
    }

    /// <summary>
    /// Tira a foto <paramref name="photoId"/> e a devolve (para apagar a imagem do bucket). As
    /// fotos de trás sobem uma posição: a fila continua 1..n, sem buraco.
    /// </summary>
    public Result<ListingPhoto> RemovePhoto(ListingPhotoId photoId, DateTimeOffset now)
    {
        var photo = _photos.Find(candidate => candidate.Id == photoId);
        if (photo is null)
            return Result<ListingPhoto>.Failure(ListingsErrors.PhotoNotFound(photoId));

        _photos.Remove(photo);
        Renumber([.. _photos.OrderBy(remaining => remaining.Position)]);
        UpdatedAt = now;

        return Result<ListingPhoto>.Success(photo);
    }

    /// <summary>
    /// Põe as fotos na ordem de <paramref name="photoIds"/>: a primeira vira a capa (posição 1).
    /// Id que não é foto deste cantinho: <c>LISTING_PHOTO_NOT_FOUND</c> (404). A lista precisa ter
    /// <b>todas</b> as fotos, cada uma uma vez; faltando ou repetida: <c>VALIDATION_ERROR</c> em
    /// <c>photoIds</c>. Mesma ordem de agora não é alteração. Devolve se mudou.
    /// </summary>
    public Result<bool> ReorderPhotos(IReadOnlyList<ListingPhotoId> photoIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(photoIds);

        // Id que não é foto deste cantinho é 404, como nas outras rotas de foto.
        var unknown = photoIds.Where(id => !HasPhoto(id)).Select(id => (ListingPhotoId?)id).FirstOrDefault();
        if (unknown is { } missing)
            return Result<bool>.Failure(ListingsErrors.PhotoNotFound(missing));

        var isSameSet = photoIds.Count == _photos.Count && photoIds.Distinct().Count() == photoIds.Count;
        if (!isSameSet)
            return Result<bool>.Failure(ListingsErrors.PhotoOrderInvalid);

        var ordered = photoIds.Select(id => _photos.First(photo => photo.Id == id)).ToList();
        if (ordered.Select(photo => photo.Position).SequenceEqual(Enumerable.Range(1, ordered.Count)))
            return Result<bool>.Success(false);

        Renumber(ordered);
        UpdatedAt = now;

        return Result<bool>.Success(true);
    }

    /// <summary>Posições 1..n na ordem da lista.</summary>
    private static void Renumber(List<ListingPhoto> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].MoveTo(index + 1);
    }

    /// <summary>Desativa: some da busca e de quem não é o dono. Idempotente.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// A conta do anfitrião foi excluída (LGPD): desativa, tira todas as fotos, apaga do endereço o
    /// que leva à casa (<see cref="ListingAddress.Redact"/>) e troca a coordenada exata pela
    /// aproximada. Título, descrição e preço ficam: reservas e avaliações antigas apontam para o
    /// cantinho. Devolve as URLs tiradas, para apagar do bucket. Idempotente.
    /// </summary>
    public IReadOnlyList<string> Erase(DateTimeOffset now)
    {
        var urls = _photos.Select(photo => photo.Url).ToList();
        var address = Address.Redact();

        if (urls.Count > 0 || address != Address || Location != ApproximateLocation)
        {
            _photos.Clear();
            Address = address;
            Location = ApproximateLocation;
            UpdatedAt = now;
        }

        Deactivate(now);

        return urls;
    }

    /// <summary>Reativa. Idempotente.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;
    }
}
