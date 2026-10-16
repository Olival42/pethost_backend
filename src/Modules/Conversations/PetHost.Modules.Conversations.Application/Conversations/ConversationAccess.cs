using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>
/// Quem pode ver e escrever em qual conversa: só o tutor e o anfitrião dela. Conversa de outra
/// pessoa responde igual a inexistente, <c>404 CONVERSATION_NOT_FOUND</c> (§8).
/// </summary>
public static class ConversationAccess
{
    /// <summary>
    /// O lado da conta logada, pelo papel do token: <c>host</c> fala como anfitrião; <c>owner</c>,
    /// como tutor. Sem o perfil do papel: <c>404 CONVERSATION_OWNER_NOT_FOUND</c> /
    /// <c>CONVERSATION_HOST_NOT_FOUND</c>. Com <paramref name="requireAvailable"/>, perfil inativo
    /// ou suspenso: <c>403 CONVERSATION_OWNER_DEACTIVATED</c> / <c>CONVERSATION_HOST_DEACTIVATED</c>.
    /// </summary>
    public static async Task<Result<ConversationParticipant>> ResolveParticipantAsync(
        IConversationParties conversationParties,
        Guid userId,
        string role,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversationParties);

        var side = role == Roles.Host ? ConversationParty.Host : ConversationParty.Owner;

        var profile = side == ConversationParty.Host
            ? await conversationParties.FindHostByAccountAsync(userId, cancellationToken).ConfigureAwait(false)
            : await conversationParties.FindOwnerByAccountAsync(userId, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<ConversationParticipant>.Failure(
                side == ConversationParty.Host ? ConversationsErrors.HostNotFound : ConversationsErrors.OwnerNotFound);
        }

        if (requireAvailable && !profile.IsAvailable)
        {
            return Result<ConversationParticipant>.Failure(
                side == ConversationParty.Host ? ConversationsErrors.HostDeactivated : ConversationsErrors.OwnerDeactivated);
        }

        return Result<ConversationParticipant>.Success(new ConversationParticipant(side, profile.Id));
    }

    /// <summary>
    /// A conversa <paramref name="conversationId"/>, rastreada, se quem pede é um dos dois lados
    /// dela. Senão, <c>404 CONVERSATION_NOT_FOUND</c>.
    /// </summary>
    public static async Task<Result<ParticipantConversation>> LoadAsync(
        IConversationRepository conversationRepository,
        IConversationParties conversationParties,
        Guid userId,
        string role,
        Guid conversationId,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversationRepository);

        var participant = await ResolveParticipantAsync(conversationParties, userId, role, requireAvailable, cancellationToken)
            .ConfigureAwait(false);

        if (participant.IsFailure)
            return Result<ParticipantConversation>.FromFailure(participant);

        var id = new ConversationId(conversationId);
        var conversation = await conversationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (conversation is null || !conversation.HasParticipant(participant.Value!.Side, participant.Value.ProfileId))
            return Result<ParticipantConversation>.Failure(ConversationsErrors.NotFound(id));

        return Result<ParticipantConversation>.Success(new ParticipantConversation(conversation, participant.Value));
    }

    /// <summary>
    /// A conta não tem o perfil do papel do token (anfitrião que ainda não foi aprovado). Nas
    /// leituras da própria caixa (lista e contador), isso não é erro: ainda não há conversa.
    /// </summary>
    public static bool IsMissingProfile(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.FirstError == ConversationsErrors.OwnerNotFound || result.FirstError == ConversationsErrors.HostNotFound;
    }

    /// <summary>
    /// Conversa bloqueada não recebe mensagem nova: pela moderação,
    /// <c>422 CONVERSATION_BLOCKED_BY_MODERATION</c>; por um dos lados, <c>422 CONVERSATION_BLOCKED</c>. Conferido antes de gravar (na foto, antes de enviar ao
    /// bucket) e de novo dentro da trava da conversa, sobre a conversa relida.
    /// </summary>
    public static Result EnsureNotBlocked(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.IsBlockedByModeration)
            return Result.Failure(ConversationsErrors.BlockedByModeration);

        return conversation.IsBlocked ? Result.Failure(ConversationsErrors.Blocked) : Result.Success();
    }

    /// <summary>
    /// Quem foi suspenso pelo admin não recebe mensagens, nos dois sentidos: o tutor não escreve
    /// para anfitrião suspenso, nem o anfitrião para tutor suspenso
    /// (<c>403 CONVERSATION_RECIPIENT_SUSPENDED</c>). Inativo continua recebendo: ele reativa sozinho
    /// e lê depois. Conferido antes de gravar e, na foto, antes de enviar ao bucket.
    /// </summary>
    public static async Task<Result> EnsureRecipientNotSuspendedAsync(
        IConversationParties conversationParties,
        Conversation conversation,
        ConversationParty sender,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversationParties);
        ArgumentNullException.ThrowIfNull(conversation);

        var recipient = ConversationPartyValues.Other(sender);
        var profileId = recipient == ConversationParty.Owner ? conversation.OwnerId : conversation.HostId;
        var suspended = await conversationParties.IsSuspendedAsync(recipient, profileId, cancellationToken).ConfigureAwait(false);

        return suspended ? Result.Failure(ConversationsErrors.RecipientSuspended) : Result.Success();
    }
}
