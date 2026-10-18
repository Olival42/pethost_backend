using PetHost.Modules.Conversations.Application.Conversations.EraseConversationsOfParty;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Infrastructure.Privacy;

/// <summary>
/// A parte do Conversations na exclusão da conta (§5, <see cref="IPersonalDataEraser"/>): as
/// conversas do tutor ou do anfitrião. Anfitrião ainda sem perfil não tem conversa.
/// </summary>
internal sealed class ConversationPersonalDataEraser(
    ICommandHandler<EraseConversationsOfPartyCommand, int> eraseConversations) : IPersonalDataEraser
{
    public async Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.ProfileId is not { } partyId)
            return;

        var party = account.Role == Roles.Owner ? ConversationParty.Owner : ConversationParty.Host;

        await eraseConversations
            .HandleAsync(new EraseConversationsOfPartyCommand(party, partyId), cancellationToken)
            .ConfigureAwait(false);
    }
}
