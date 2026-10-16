using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;

/// <summary>
/// Apaga as conversas sem atividade desde o prazo (a última mensagem ou, sem nenhuma, a abertura),
/// com as mensagens e as fotos do bucket. Conversa de dupla com reserva em aberto fica: o combinado
/// de uma estadia marcada para daqui a meses não some.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>As URLs das fotos são lidas antes; o banco apaga a conversa só se ela continua parada
/// (<see cref="IConversationRepository.DeleteIfInactiveAsync"/>). Mensagem que chegou no meio salva
/// a conversa, e aí as fotos também ficam.</item>
/// <item>As fotos saem do bucket depois que a conversa saiu do banco: falha no bucket deixa um
/// arquivo solto, nunca uma mensagem apontando para foto apagada.</item>
/// <item>A rodada pagina pelas conversas que ficaram (as puladas), até acabar.</item>
/// </list>
/// </remarks>
public sealed class PurgeInactiveConversationsCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IConversationBookings conversationBookings,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<PurgeInactiveConversationsCommand, PurgeInactiveConversationsResponse>
{
    public async Task<Result<PurgeInactiveConversationsResponse>> HandleAsync(
        PurgeInactiveConversationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cutoff = timeProvider.GetUtcNow().AddDays(-command.InactivityDays);
        var purged = 0;
        var kept = 0;

        while (true)
        {
            var batch = await conversationRepository
                .ListInactiveAsync(cutoff, kept, command.BatchSize, cancellationToken)
                .ConfigureAwait(false);

            foreach (var conversation in batch)
            {
                if (await conversationBookings.HasOpenBookingAsync(conversation.OwnerId, conversation.HostId, cancellationToken).ConfigureAwait(false))
                {
                    kept++;
                    continue;
                }

                if (await PurgeAsync(conversation, cutoff, cancellationToken).ConfigureAwait(false))
                    purged++;
            }

            if (batch.Count < command.BatchSize)
                break;
        }

        return Result<PurgeInactiveConversationsResponse>.Success(new PurgeInactiveConversationsResponse(purged));
    }

    /// <summary>Apaga uma conversa e as fotos dela. Devolve se apagou (mensagem nova a salva).</summary>
    private async Task<bool> PurgeAsync(Conversation conversation, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var photos = await messageRepository.ListImageUrlsAsync(conversation.Id, cancellationToken).ConfigureAwait(false);

        var deleted = await conversationRepository
            .DeleteIfInactiveAsync(conversation.Id, cutoff, cancellationToken)
            .ConfigureAwait(false);

        if (!deleted)
            return false;

        foreach (var photo in photos)
            await imageStorage.DeleteAsync(photo, CancellationToken.None).ConfigureAwait(false);

        await ConversationTrail.RecordPurgedAsync(auditTrail, conversation, photos.Count, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
