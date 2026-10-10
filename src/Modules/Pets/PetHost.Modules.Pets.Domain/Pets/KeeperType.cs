namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>De quem é o pet: de um tutor ou de um anfitrião (o animal que mora na casa dele).</summary>
public enum KeeperType
{
    /// <summary>Tutor: <c>pets.owner_id</c> → <c>owner.owners.id</c>.</summary>
    Owner,

    /// <summary>Anfitrião: <c>pets.host_id</c> → <c>host.hosts.id</c>.</summary>
    Host,
}
