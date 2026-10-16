using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Conversations.Application.Conversations.GetConversationById;
using PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;
using PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>
/// Chat entre o tutor e o anfitrião: uma conversa por dupla, com texto e foto. Quem pede sai sempre
/// do token — nunca do corpo — e o lado (tutor ou anfitrião) sai do papel do token. As mensagens
/// novas e o contador de não lidas também chegam em tempo real pelo hub <c>/hubs/conversations</c>.
/// </summary>
[ApiController]
[Route("api/v1/conversations")]
public sealed class ConversationsController(
    ICommandHandler<StartConversationCommand, StartedConversation> startConversation,
    ICommandHandler<SendMessageCommand, MessageResponse> sendMessage,
    ICommandHandler<SendPhotoMessageCommand, MessageResponse> sendPhotoMessage,
    ICommandHandler<MarkConversationReadCommand, UnreadCountResponse> markConversationRead,
    IQueryHandler<ListMyConversationsQuery, PagedResult<ConversationResponse>> listMyConversations,
    IQueryHandler<GetConversationByIdQuery, ConversationResponse> getConversationById,
    IQueryHandler<GetUnreadMessageCountQuery, UnreadCountResponse> getUnreadMessageCount,
    IQueryHandler<ListConversationMessagesQuery, MessagePageResponse> listConversationMessages) : ControllerBase
{
    private const string Route = "/api/v1/conversations";

    /// <summary>O tutor puxa conversa com o anfitrião do cantinho ("Puxar conversa").</summary>
    /// <remarks>
    /// Corpo: <c>listingId</c>. Uma conversa por dupla tutor–anfitrião: a primeira vez cria
    /// (<c>201</c> + <c>Location</c>); depois, devolve a mesma (<c>200</c>). Cantinho inexistente ou
    /// fora da busca: 404 <c>CONVERSATION_LISTING_NOT_FOUND</c>. Conta sem perfil de tutor: 404
    /// <c>CONVERSATION_OWNER_NOT_FOUND</c>. Tutor inativo ou suspenso: 403
    /// <c>CONVERSATION_OWNER_DEACTIVATED</c>.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartAsync([FromBody] StartConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await startConversation.HandleAsync(
            new StartConversationCommand(User.GetUserId(), request.ListingId),
            cancellationToken);

        var conversation = result.Map(started => started.Conversation);

        return result.IsSuccess && result.Value!.Created
            ? conversation.ToCreatedResult($"{Route}/{conversation.Value!.Id}")
            : conversation.ToActionResult();
    }

    /// <summary>As conversas da conta logada: do tutor ou do anfitrião, pelo papel do token.</summary>
    /// <remarks>
    /// Da atividade mais recente para a mais antiga, cada uma com a última mensagem e as não lidas
    /// (<c>unreadCount</c>). Paginado: <c>page</c> a partir de 1, <c>pageSize</c> até 100. Anfitrião
    /// ainda não aprovado (sem perfil): lista vazia.
    /// </remarks>
    [HttpGet("me")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ConversationResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListMineAsync([FromQuery] MyConversationsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listMyConversations.HandleAsync(
            new ListMyConversationsQuery(User.GetUserId(), CurrentRole(), request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O contador de não lidas da conta logada, somando todas as conversas.</summary>
    /// <remarks>Anfitrião ainda não aprovado (sem perfil): <c>0</c>.</remarks>
    [HttpGet("me/unread-count")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        var result = await getUnreadMessageCount.HandleAsync(
            new GetUnreadMessageCountQuery(User.GetUserId(), CurrentRole()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Uma conversa. Só o tutor e o anfitrião dela.</summary>
    /// <remarks>Conversa de outra pessoa: 404 <c>CONVERSATION_NOT_FOUND</c>, como se não existisse.</remarks>
    [HttpGet("{conversationId:guid}")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await getConversationById.HandleAsync(
            new GetConversationByIdQuery(User.GetUserId(), CurrentRole(), conversationId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>As mensagens da conversa, da mais nova para a mais antiga.</summary>
    /// <remarks>
    /// Paginação por cursor: sem <c>before</c>, as mais recentes; para as anteriores, mande em
    /// <c>before</c> o id da última mensagem recebida. <c>hasMore</c> diz se há mais antigas.
    /// <c>pageSize</c> de 1 a 100 (padrão 50). <c>before</c> que não é mensagem desta conversa: 400
    /// no campo. Ler não marca como lida (<c>PUT .../read-marker</c>).
    /// </remarks>
    [HttpGet("{conversationId:guid}/messages")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<MessagePageResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MessagePageResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<MessagePageResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessagePageResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessagePageResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMessagesAsync(
        Guid conversationId,
        [FromQuery] ConversationMessagesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await listConversationMessages.HandleAsync(
            new ListConversationMessagesQuery(User.GetUserId(), CurrentRole(), conversationId, request.Before, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Envia uma mensagem de texto. O outro lado recebe na hora pelo hub.</summary>
    /// <remarks>
    /// Corpo: <c>body</c>, até 2.000 caracteres, sem caractere de controle (quebra de linha e
    /// tabulação passam). O lado de quem envia sai do papel do token. Perfil inativo ou suspenso:
    /// 403 <c>CONVERSATION_OWNER_DEACTIVATED</c> / <c>CONVERSATION_HOST_DEACTIVATED</c>. Outro lado
    /// suspenso: 403 <c>CONVERSATION_RECIPIENT_SUSPENDED</c>. Durante uma estadia em andamento, a
    /// mensagem leva o <c>bookingId</c> (histórico da reserva). Limite próprio
    /// (<c>chat-messages</c>, texto e foto juntos): 429 <c>TOO_MANY_REQUESTS</c>.
    /// </remarks>
    [HttpPost("{conversationId:guid}/messages")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [EnableRateLimiting(RateLimitPolicies.ChatMessages)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendAsync(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await sendMessage.HandleAsync(
            new SendMessageCommand(User.GetUserId(), CurrentRole(), conversationId, request.Body),
            cancellationToken);

        return result.ToCreatedResult($"{Route}/{conversationId}/messages");
    }

    /// <summary>Envia uma foto, com legenda opcional. O outro lado recebe na hora pelo hub.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem na parte <c>file</c> (JPG/JPEG, PNG ou WebP, até 5 MB;
    /// os metadados, como GPS, são removidos) e a legenda opcional na parte <c>body</c>. A foto vai
    /// para o bucket público. Conversa de outra pessoa (404) ou outro lado suspenso
    /// (403 <c>CONVERSATION_RECIPIENT_SUSPENDED</c>): nada é enviado ao bucket. Arquivo inválido: 400
    /// no campo <c>file</c>. Conta no mesmo limite das mensagens de texto (<c>chat-messages</c>).
    /// </remarks>
    [HttpPost("{conversationId:guid}/messages/photo")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [EnableRateLimiting(RateLimitPolicies.ChatMessages)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ApiResponse<MessageResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendPhotoAsync(
        Guid conversationId,
        IFormFile? file,
        [FromForm] string? body,
        CancellationToken cancellationToken)
    {
        var result = await sendPhotoMessage.HandleAsync(
            new SendPhotoMessageCommand(User.GetUserId(), CurrentRole(), conversationId, file.ToImageUpload(), body),
            cancellationToken);

        return result.ToCreatedResult($"{Route}/{conversationId}/messages");
    }

    /// <summary>Marca como lidas as mensagens que a conta recebeu na conversa.</summary>
    /// <remarks>
    /// Chamado quando o app abre a conversa. Devolve o contador novo da conta (<c>total</c>), que
    /// também vai pelo hub para as outras abas e aparelhos. Idempotente. O remetente não vê se a
    /// mensagem foi lida.
    /// </remarks>
    [HttpPut("{conversationId:guid}/read-marker")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<UnreadCountResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkReadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await markConversationRead.HandleAsync(
            new MarkConversationReadCommand(User.GetUserId(), CurrentRole(), conversationId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>O lado de quem pede: o anfitrião fala como anfitrião; o resto, como tutor.</summary>
    private string CurrentRole() => User.IsInRole(Roles.Host) ? Roles.Host : Roles.Owner;
}
