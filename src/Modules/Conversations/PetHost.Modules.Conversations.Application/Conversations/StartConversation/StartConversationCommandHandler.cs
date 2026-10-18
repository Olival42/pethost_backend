using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.StartConversation;

/// <summary>
/// Abre a conversa do tutor com o anfitrião do cantinho, ou devolve a que a dupla já tem. Só o
/// tutor puxa conversa, e precisa estar ativo e sem suspensão (<c>403 CONVERSATION_OWNER_DEACTIVATED</c>).
/// O cantinho precisa aparecer na busca; senão, <c>404 CONVERSATION_LISTING_NOT_FOUND</c>. A pessoa
/// não conversa com a própria conta de anfitrião (mesmo e-mail): <c>422 CONVERSATION_OWN_LISTING</c>.
/// </summary>
/// <remarks>
/// Duas aberturas ao mesmo tempo (duas abas) esbarram no índice único da dupla: a segunda recebe
/// a conversa que a primeira gravou. A trilha registra só a conversa que nasceu.
/// </remarks>
public sealed class StartConversationCommandHandler(
    IConversationRepository conversationRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationListings conversationListings,
    IConversationQueries conversationQueries,
    ConversationResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<StartConversationCommand, StartedConversation>
{
    public async Task<Result<StartedConversation>> HandleAsync(StartConversationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var owner = await ConversationAccess
            .ResolveParticipantAsync(conversationParties, command.UserId, Roles.Owner, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (owner.IsFailure)
            return Result<StartedConversation>.FromFailure(owner);

        var hostId = await conversationListings.FindListableHostIdAsync(command.ListingId, cancellationToken).ConfigureAwait(false);
        if (hostId is null)
            return Result<StartedConversation>.Failure(ConversationsErrors.ListingNotFound(command.ListingId));

        if (await IsSamePersonAsync(owner.Value!.ProfileId, hostId.Value, cancellationToken).ConfigureAwait(false))
            return Result<StartedConversation>.Failure(ConversationsErrors.OwnListing);

        var existing = await conversationRepository
            .FindByPartiesAsync(owner.Value!.ProfileId, hostId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            return await RespondAsync(existing.Id, created: false, cancellationToken).ConfigureAwait(false);

        var conversation = Conversation.Start(owner.Value.ProfileId, hostId.Value, timeProvider.GetUtcNow());
        conversationRepository.Add(conversation);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return await RecoverAsync(saved, owner.Value.ProfileId, hostId.Value, cancellationToken).ConfigureAwait(false);

        await ConversationTrail.RecordStartedAsync(auditTrail, conversation, cancellationToken).ConfigureAwait(false);

        return await RespondAsync(conversation.Id, created: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tutor e anfitrião são a mesma pessoa? As contas são separadas, mas o e-mail igual liga as duas
    /// (troca de conta, restrição 9 do CPF).
    /// </summary>
    private async Task<bool> IsSamePersonAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken)
    {
        var owners = await conversationParties.GetContactsAsync(ConversationParty.Owner, [ownerId], cancellationToken).ConfigureAwait(false);
        var hosts = await conversationParties.GetContactsAsync(ConversationParty.Host, [hostId], cancellationToken).ConfigureAwait(false);

        return owners.TryGetValue(ownerId, out var owner)
            && hosts.TryGetValue(hostId, out var host)
            && string.Equals(owner.Email, host.Email, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Perdeu a corrida para outra abertura da mesma dupla: devolve a conversa que ficou.</summary>
    private async Task<Result<StartedConversation>> RecoverAsync(
        Result saved,
        Guid ownerId,
        Guid hostId,
        CancellationToken cancellationToken)
    {
        if (saved.FirstError != ConversationsErrors.AlreadyExists)
            return Result<StartedConversation>.FromFailure(saved);

        var winner = await conversationRepository.FindByPartiesAsync(ownerId, hostId, cancellationToken).ConfigureAwait(false);

        return winner is null
            ? Result<StartedConversation>.FromFailure(saved)
            : await RespondAsync(winner.Id, created: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<StartedConversation>> RespondAsync(ConversationId id, bool created, CancellationToken cancellationToken)
    {
        var row = await conversationQueries.GetAsync(id, ConversationParty.Owner, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return Result<StartedConversation>.Failure(ConversationsErrors.NotFound(id));

        var response = await responseBuilder.BuildAsync(row, cancellationToken).ConfigureAwait(false);

        return Result<StartedConversation>.Success(new StartedConversation(response, created));
    }
}
