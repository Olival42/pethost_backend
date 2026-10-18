using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Abstractions;

/// <summary>
/// Porta para os dois lados da avaliação: o tutor (módulo Owners), o anfitrião (Hosts), o nome, a
/// foto e o e-mail da conta (Auth) e o nome dos pets (Pets). §5: a Application declara a interface;
/// a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IReviewParties
{
    /// <summary>O tutor da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<ReviewerProfile?> FindOwnerByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>O anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<ReviewerProfile?> FindHostByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Como aparece cada perfil do lado <paramref name="side"/>: o tutor pelo primeiro nome; o
    /// anfitrião pelo primeiro nome (pessoa física) ou pelo nome fantasia (empresa). Quem sumiu não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ReviewPartyInfo>> GetInfoAsync(
        ReviewParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken);

    /// <summary>E-mail, nome e se pode receber aviso de cada perfil do lado <paramref name="side"/>. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, ReviewContact>> GetContactsAsync(
        ReviewParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken);

    /// <summary>O nome de cada pet. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPetNamesAsync(
        IReadOnlyCollection<Guid> petIds,
        CancellationToken cancellationToken);
}

/// <summary>O perfil (tutor ou anfitrião) da conta logada.</summary>
/// <param name="Id">Id do perfil (<c>owner.owners.id</c> ou <c>host.hosts.id</c>).</param>
/// <param name="IsAvailable">Ativo e sem suspensão.</param>
public sealed record ReviewerProfile(Guid Id, bool IsAvailable);

/// <summary>Como um lado aparece numa avaliação: nome curto e foto. Nunca o nome completo do tutor.</summary>
public sealed record ReviewPartyInfo(Guid ProfileId, string Name, string? AvatarUrl);

/// <summary>Para quem mandar um aviso por e-mail.</summary>
/// <param name="IsReachable">Perfil ativo, conta ativa e sem suspensão: pode receber aviso.</param>
public sealed record ReviewContact(Guid ProfileId, string Name, string Email, bool IsReachable);
