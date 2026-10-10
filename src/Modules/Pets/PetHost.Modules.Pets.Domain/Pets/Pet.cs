using PetHost.Shared.Kernel.Domain;

namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// O pet: a ficha (<see cref="Profile"/>) e de quem ele é. Espelha a tabela <c>pet.pets</c>.
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
