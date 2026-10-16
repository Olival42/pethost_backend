using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;

/// <summary>
/// Mensagens que a conta recebeu e não leu. Conta sem o perfil do papel (anfitrião ainda não
/// aprovado): <c>0</c>, para o contador do app não virar erro.
/// </summary>
public sealed class GetUnreadMessageCountQueryHandler(
    IConversationParties conversationParties,
    IConversationQueries conversationQueries) : IQueryHandler<GetUnreadMessageCountQuery, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(GetUnreadMessageCountQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var participant = await ConversationAccess
            .ResolveParticipantAsync(conversationParties, query.UserId, query.Role, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (ConversationAccess.IsMissingProfile(participant))
            return Result<UnreadCountResponse>.Success(new UnreadCountResponse(0));

        if (participant.IsFailure)
            return Result<UnreadCountResponse>.FromFailure(participant);

        var total = await conversationQueries.CountUnreadAsync(participant.Value!, cancellationToken).ConfigureAwait(false);

        return Result<UnreadCountResponse>.Success(new UnreadCountResponse(total));
    }
}
