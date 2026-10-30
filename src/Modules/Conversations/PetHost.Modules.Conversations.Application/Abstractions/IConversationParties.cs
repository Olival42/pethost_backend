using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>
/// Porta para os dois lados da conversa: o tutor (módulo Owners), o anfitrião (Hosts) e o nome,
/// a foto e o id da conta de cada um (Auth). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IConversationParties
{
    /// <summary>O tutor da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<PartyProfile?> FindOwnerByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>O anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<PartyProfile?> FindHostByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// O perfil <paramref name="profileId"/> do lado <paramref name="side"/> está suspenso pelo admin.
    /// Perfil que não existe conta como não suspenso.
    /// </summary>
    Task<bool> IsSuspendedAsync(ConversationParty side, Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// E-mail, nome e se pode receber aviso de cada perfil do lado <paramref name="side"/>, numa
    /// consulta por módulo. Quem sumiu não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PartyContact>> GetContactsAsync(
        ConversationParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken);

    /// <summary>Nome, foto e conta de cada tutor, numa consulta por módulo. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, PartyInfo>> GetOwnersAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken);

    /// <summary>Nome (empresa pelo nome fantasia), foto e conta de cada anfitrião. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, PartyInfo>> GetHostsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);
}
