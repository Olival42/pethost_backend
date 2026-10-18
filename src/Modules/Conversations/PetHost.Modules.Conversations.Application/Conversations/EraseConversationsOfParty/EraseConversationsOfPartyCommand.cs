using FluentValidation;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.EraseConversationsOfParty;

/// <summary>
/// Um dos lados excluiu a conta (LGPD): todas as conversas dele somem, com as mensagens, as
/// denúncias e as fotos do bucket — o outro lado perde o histórico. Disparada pelo contrato
/// <c>IPersonalDataEraser</c>, antes de a conta ser anonimizada.
/// </summary>
/// <param name="PartyId">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>).</param>
public sealed record EraseConversationsOfPartyCommand(ConversationParty Party, Guid PartyId) : ICommand<int>;

public sealed class EraseConversationsOfPartyCommandValidator : AbstractValidator<EraseConversationsOfPartyCommand>
{
    public EraseConversationsOfPartyCommandValidator()
    {
        RuleFor(x => x.Party).IsInEnum().WithMessage("Party is invalid.");
        RuleFor(x => x.PartyId).NotEmpty().WithMessage("Party id is required.");
    }
}

/// <summary>
/// Ao contrário da retenção, apaga sem condição: bloqueio, denúncia pendente ou reserva em aberto não
/// seguram a conversa (a exclusão já foi barrada antes com reserva em aberto). As fotos saem do
/// bucket depois que a conversa saiu do banco, como na retenção. Devolve quantas foram apagadas.
/// Idempotente.
/// </summary>
public sealed class EraseConversationsOfPartyCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IImageStorage imageStorage,
    IAuditTrail auditTrail) : ICommandHandler<EraseConversationsOfPartyCommand, int>
{
    public async Task<Result<int>> HandleAsync(EraseConversationsOfPartyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var conversations = await conversationRepository
            .ListByPartyAsync(command.Party, command.PartyId, cancellationToken)
            .ConfigureAwait(false);

        var erased = 0;

        foreach (var conversation in conversations)
        {
            var photos = await messageRepository.ListImageUrlsAsync(conversation.Id, cancellationToken).ConfigureAwait(false);

            if (!await conversationRepository.DeleteAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
                continue;

            foreach (var photo in photos)
                await imageStorage.DeleteAsync(photo, CancellationToken.None).ConfigureAwait(false);

            await ConversationTrail.RecordErasedAsync(auditTrail, conversation, photos.Count, cancellationToken).ConfigureAwait(false);
            erased++;
        }

        return Result<int>.Success(erased);
    }
}
