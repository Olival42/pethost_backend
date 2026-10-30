using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;

/// <summary>
/// Paginada (§13), da atividade mais recente para a mais antiga, com a última mensagem e as não
/// lidas de cada uma. Paginação inválida: <c>400</c> com os dois campos. Perfil inativo continua
/// vendo o histórico. Conta sem o perfil do papel (anfitrião ainda não aprovado): lista vazia, para
/// o app não tratar como erro.
/// </summary>
public sealed class ListMyConversationsQueryHandler(
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    ConversationResponseBuilder responseBuilder) : IQueryHandler<ListMyConversationsQuery, PagedResult<ConversationResponse>>
{
    public async Task<Result<PagedResult<ConversationResponse>>> HandleAsync(
        ListMyConversationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();

        if (query.Page < 1)
            errors.Add(ConversationsErrors.PageInvalid);

        if (query.PageSize is < 1 or > ConversationsErrors.MaxPageSize)
            errors.Add(ConversationsErrors.PageSizeInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<ConversationResponse>>.Failure(errors);

        var participant = await ConversationAccess
            .ResolveParticipantAsync(conversationParties, query.UserId, query.Role, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (ConversationAccess.IsMissingProfile(participant))
            return Result<PagedResult<ConversationResponse>>.Success(new PagedResult<ConversationResponse>([], query.Page, query.PageSize, 0));

        if (participant.IsFailure)
            return Result<PagedResult<ConversationResponse>>.FromFailure(participant);

        var (rows, total) = await conversationQueries
            .ListAsync(participant.Value!, query.Page, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        var items = await responseBuilder.BuildAsync(rows, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ConversationResponse>>.Success(
            new PagedResult<ConversationResponse>(items, query.Page, query.PageSize, total));
    }
}
