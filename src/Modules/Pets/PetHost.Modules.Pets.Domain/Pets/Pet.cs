using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// O pet: a ficha (<see cref="Profile"/>), as fotos (<see cref="Photos"/>) e de quem ele é.
/// Espelha as tabelas <c>pet.pets</c> e <c>pet.pet_photos</c>.
/// </summary>
/// <remarks>
/// Pertence a <b>um</b> tutor ou a <b>um</b> anfitrião — nunca aos dois, nunca a ninguém.
/// O tutor cadastra os pets que vai hospedar; o anfitrião, os que moram na casa dele, para
/// o tutor saber com quem o pet vai conviver. Guarda o id do tutor ou do anfitrião (FK
/// lógica para o módulo dele), não o da conta.
/// <para>
/// Pet com histórico não é apagado: é desativado (<see cref="Deactivate"/>) e pode voltar.
/// </para>
/// </remarks>
public sealed class Pet : Entity<PetId>
{
    /// <summary>Quantas fotos um pet pode ter.</summary>
    public const int MaxPhotos = 3;

    private readonly List<PetPhoto> _photos = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Pet()
    {
        Profile = null!;
    }

    private Pet(PetKeeper keeper, PetProfile profile, DateTimeOffset now)
        : base(PetId.New())
    {
        OwnerId = keeper.Type == KeeperType.Owner ? keeper.Id : null;
        HostId = keeper.Type == KeeperType.Host ? keeper.Id : null;
        Profile = profile;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Tutor dono do pet (<c>owner.owners.id</c>). Nulo quando o pet é de um anfitrião.</summary>
    public Guid? OwnerId { get; private set; }

    /// <summary>Anfitrião dono do pet (<c>host.hosts.id</c>). Nulo quando o pet é de um tutor.</summary>
    public Guid? HostId { get; private set; }

    /// <summary>De quem é o pet. Sempre exatamente um dos dois ids.</summary>
    public PetKeeper Keeper =>
        OwnerId is { } ownerId ? new PetKeeper(KeeperType.Owner, ownerId) : new PetKeeper(KeeperType.Host, HostId ?? Guid.Empty);

    public PetProfile Profile { get; private set; }

    /// <summary>Até <see cref="MaxPhotos"/> fotos, pela vaga: a primeira é a capa.</summary>
    public IReadOnlyList<PetPhoto> Photos => [.. _photos.OrderBy(photo => photo.Position)];

    /// <summary>Ainda cabe foto? Conferido antes de enviar a imagem ao bucket.</summary>
    public bool CanAddPhoto => _photos.Count < MaxPhotos;

    /// <summary>Falso depois de desativado. Nasce ativo.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Quando foi desativado pela última vez. Nulo enquanto ativo.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Cadastra o pet de <paramref name="keeper"/>, já com a ficha validada.</summary>
    public static Pet Create(PetKeeper keeper, PetProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (keeper.Id == Guid.Empty)
            throw new ArgumentException("The pet must belong to an owner or a host.", nameof(keeper));

        return new Pet(keeper, profile, now);
    }

    /// <summary>O pet é de <paramref name="keeper"/>?</summary>
    public bool IsKeptBy(PetKeeper keeper) => Keeper == keeper;

    /// <summary>
    /// Troca a ficha. Ficha igual à atual não é alteração: <see cref="UpdatedAt"/> só anda
    /// se algo mudou. Devolve se mudou. Vale também para pet desativado: o dono pode
    /// corrigir a ficha sem reativar.
    /// </summary>
    public bool UpdateProfile(PetProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile == Profile)
            return false;

        Profile = profile;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Põe uma foto na primeira vaga livre. A URL e o hash vêm do bucket de imagens, nunca do
    /// cliente. Vale também para pet desativado. Sem vaga: <c>PET_PHOTO_LIMIT_REACHED</c>;
    /// imagem que o pet já tem: <c>PET_PHOTO_ALREADY_EXISTS</c>.
    /// </summary>
    public Result<PetPhoto> AddPhoto(string url, string contentHash, DateTimeOffset now)
    {
        if (!CanAddPhoto)
            return Result<PetPhoto>.Failure(PetsErrors.PhotoLimitReached);

        if (HasImage(contentHash))
            return Result<PetPhoto>.Failure(PetsErrors.PhotoAlreadyExists);

        var position = Enumerable.Range(1, MaxPhotos).First(slot => _photos.TrueForAll(photo => photo.Position != slot));

        var photo = PetPhoto.Create(Id, url, contentHash, position, now);
        if (photo.IsFailure)
            return photo;

        _photos.Add(photo.Value!);
        UpdatedAt = now;

        return photo;
    }

    /// <summary>O pet já tem esta imagem (mesmo hash) em alguma foto?</summary>
    public bool HasImage(string contentHash) =>
        _photos.Exists(photo => string.Equals(photo.ContentHash, contentHash, StringComparison.Ordinal));

    /// <summary>A foto <paramref name="photoId"/> é deste pet?</summary>
    public bool HasPhoto(PetPhotoId photoId) => _photos.Exists(photo => photo.Id == photoId);

    /// <summary>
    /// Troca a imagem da foto <paramref name="photoId"/>, mantendo o id e a vaga. Devolve a
    /// URL anterior (para apagar a imagem do bucket). Imagem que o pet já tem — nesta ou em
    /// outra foto —: <c>PET_PHOTO_ALREADY_EXISTS</c>.
    /// </summary>
    public Result<string> ReplacePhoto(PetPhotoId photoId, string url, string contentHash, DateTimeOffset now)
    {
        var photo = _photos.Find(candidate => candidate.Id == photoId);
        if (photo is null)
            return Result<string>.Failure(PetsErrors.PhotoNotFound(photoId));

        if (HasImage(contentHash))
            return Result<string>.Failure(PetsErrors.PhotoAlreadyExists);

        var previous = photo.ChangeImage(url, contentHash);
        if (previous.IsSuccess)
            UpdatedAt = now;

        return previous;
    }

    /// <summary>Tira a foto <paramref name="photoId"/> e a devolve (para apagar a imagem do bucket).</summary>
    public Result<PetPhoto> RemovePhoto(PetPhotoId photoId, DateTimeOffset now)
    {
        var photo = _photos.Find(candidate => candidate.Id == photoId);
        if (photo is null)
            return Result<PetPhoto>.Failure(PetsErrors.PhotoNotFound(photoId));

        _photos.Remove(photo);
        UpdatedAt = now;

        return Result<PetPhoto>.Success(photo);
    }

    /// <summary>Desativa. Idempotente: desativar um pet inativo não muda nada.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Reativa. Idempotente: reativar um pet ativo não muda nada.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;
    }
}
