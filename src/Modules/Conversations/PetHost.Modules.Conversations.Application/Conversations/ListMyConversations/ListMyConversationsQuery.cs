using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;

/// <summary>A tela "Conversas" da conta logada: do tutor ou do anfitrião, pelo papel do token.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="PageSize">De 1 a 100.</param>
public sealed record ListMyConversationsQuery(
    Guid UserId,
    string Role,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ConversationResponse>>;
