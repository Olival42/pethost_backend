using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>Quem pede, já conferido: o lado (pelo papel do token) e o id do perfil desse lado.</summary>
/// <param name="ProfileId">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>).</param>
public sealed record ConversationParticipant(ConversationParty Side, Guid ProfileId);
