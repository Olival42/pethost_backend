namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>
/// Quem é o dono do pet: um tutor (id do tutor, módulo Owners) ou um anfitrião (id do
/// anfitrião, módulo Hosts). Nunca o id da conta. FK lógica, sem constraint física (§5).
/// </summary>
public readonly record struct PetKeeper(KeeperType Type, Guid Id)
{
    public static PetKeeper Owner(Guid ownerId) => Create(KeeperType.Owner, ownerId);

    public static PetKeeper Host(Guid hostId) => Create(KeeperType.Host, hostId);

    /// <summary>Texto da resposta: <c>owner</c> ou <c>host</c>.</summary>
    public string TypeName => Type == KeeperType.Owner ? "owner" : "host";

    private static PetKeeper Create(KeeperType type, Guid id)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("The keeper id cannot be empty.", nameof(id));

        return new PetKeeper(type, id);
    }
}
