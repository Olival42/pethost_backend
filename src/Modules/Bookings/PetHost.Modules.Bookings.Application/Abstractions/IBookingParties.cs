namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>
/// Porta para os dois lados da reserva: o tutor (módulo Owners), o anfitrião (Hosts) e o nome, a
/// foto e o telefone da conta de cada um (Auth). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IBookingParties
{
    /// <summary>O tutor da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<PartyProfile?> FindOwnerByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>O anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<PartyProfile?> FindHostByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Nome, foto, e-mail e telefone de cada tutor, numa consulta por módulo. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, PartyInfo>> GetOwnersAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken);

    /// <summary>Nome (empresa pelo nome fantasia), foto, e-mail e telefone de cada anfitrião. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, PartyInfo>> GetHostsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);
}

/// <summary>O tutor ou o anfitrião da conta logada.</summary>
/// <param name="Id">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>), nunca o da conta.</param>
/// <param name="IsAvailable">Ativo e sem suspensão.</param>
public sealed record PartyProfile(Guid Id, bool IsAvailable);

/// <summary>
/// Um lado da reserva com o telefone e o e-mail. O telefone só sai na resposta como contato,
/// depois da confirmação (escopo, regra 7); o e-mail nunca sai — só recebe os avisos da reserva.
/// </summary>
/// <param name="Id">Id do tutor ou do anfitrião — nunca o da conta.</param>
public sealed record PartyInfo(Guid Id, string Name, string? AvatarUrl, string? Phone, string Email)
{
    /// <summary>Só nome e foto: o que qualquer lado vê do outro.</summary>
    public PartySummary ToSummary() => new(Id, Name, AvatarUrl);
}

/// <summary>Quem é o tutor ou o anfitrião, para exibição. Sem e-mail, telefone nem endereço.</summary>
/// <param name="Id">Id do tutor ou do anfitrião — nunca o da conta.</param>
/// <param name="Name">Nome da conta; empresa mostra o nome fantasia.</param>
public sealed record PartySummary(Guid Id, string Name, string? AvatarUrl);
