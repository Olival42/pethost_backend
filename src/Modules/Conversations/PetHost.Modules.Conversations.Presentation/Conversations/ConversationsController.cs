using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Conversations.Application.Conversations.BlockConversation;
using PetHost.Modules.Conversations.Application.Conversations.GetConversationById;
using PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;
using PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;
using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Application.Messages.GetMessagePhoto;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Modules.Conversations.Application.Preferences;
using PetHost.Modules.Conversations.Application.Preferences.GetNotificationSettings;
using PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;
using PetHost.Modules.Conversations.Application.Reports.ReportMessage;
using PetHost.Modules.Conversations.Application.Reports.Responses;
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
/// Moderação: os dois lados bloqueiam a conversa e denunciam a mensagem que receberam (a fila do
/// admin fica em <c>ConversationReportsAdminController</c>).
/// </summary>
[ApiController]
[Route("api/v1/conversations")]
public sealed class ConversationsController(
    ICommandHandler<StartConversationCommand, StartedConversation> startConversation,
    ICommandHandler<SendMessageCommand, MessageResponse> sendMessage,
    ICommandHandler<SendPhotoMessageCommand, MessageResponse> sendPhotoMessage,
    ICommandHandler<MarkConversationReadCommand, UnreadCountResponse> markConversationRead,
    ICommandHandler<BlockConversationCommand, ConversationResponse> blockConversation,
    ICommandHandler<UnblockConversationCommand, ConversationResponse> unblockConversation,
    ICommandHandler<ReportMessageCommand, SubmittedReportResponse> reportMessage,
    ICommandHandler<UpdateNotificationSettingsCommand, NotificationSettingsResponse> updateNotificationSettings,
    IQueryHandler<GetNotificationSettingsQuery, NotificationSettingsResponse> getNotificationSettings,
    IQueryHandler<ListMyConversationsQuery, PagedResult<ConversationResponse>> listMyConversations,
    IQueryHandler<GetConversationByIdQuery, ConversationResponse> getConversationById,
    IQueryHandler<GetUnreadMessageCountQuery, UnreadCountResponse> getUnreadMessageCount,
    IQueryHandler<ListConversationMessagesQuery, MessagePageResponse> listConversationMessages,
    IQueryHandler<GetMessagePhotoQuery, MessagePhotoResponse> getMessagePhoto) : ControllerBase
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

    /// <summary>O que a conta logada recebe do chat fora do app.</summary>
    /// <remarks>
    /// <c>unreadMessagesEmail</c>: o e-mail de "mensagens não lidas" (resumo, sem o texto, depois de
    /// 15 min sem ler). Ligado por padrão. A escolha é da conta: tutor e anfitrião escolhem cada um.
    /// </remarks>
    [HttpGet("me/notification-settings")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotificationSettingsAsync(CancellationToken cancellationToken)
    {
        var result = await getNotificationSettings.HandleAsync(new GetNotificationSettingsQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Liga ou desliga o e-mail de mensagens não lidas.</summary>
    /// <remarks>Corpo: <c>{ "unreadMessagesEmail": false }</c>. Obrigatório (400 no campo). Idempotente.</remarks>
    [HttpPut("me/notification-settings")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<NotificationSettingsResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateNotificationSettingsAsync(
        [FromBody] UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await updateNotificationSettings.HandleAsync(
            new UpdateNotificationSettingsCommand(User.GetUserId(), request.UnreadMessagesEmail),
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

    /// <summary>Um link novo para a foto de uma mensagem.</summary>
    /// <remarks>
    /// As fotos do chat ficam no bucket privado: a listagem traz <c>imageUrl</c> assinada, que vence em
    /// <c>imageUrlExpiresAt</c>. Venceu (o bucket responde 403): peça outra aqui. Só quem é da conversa;
    /// a resposta vai com <c>Cache-Control: no-store</c>.
    /// </remarks>
    [HttpGet("{conversationId:guid}/messages/{messageId:guid}/photo")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<MessagePhotoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MessagePhotoResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessagePhotoResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessagePhotoResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessagePhotoAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var result = await getMessagePhoto.HandleAsync(
            new GetMessagePhotoQuery(User.GetUserId(), CurrentRole(), conversationId, messageId),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
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
    /// para o bucket privado e só abre por URL assinada. Prefira o upload direto
    /// (<c>POST /api/v1/uploads</c>, <c>purpose: chat-photo</c>): o arquivo não passa pela API. Conversa de outra pessoa (404) ou outro lado suspenso
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

    /// <summary>Bloqueia a conversa: ninguém envia até quem bloqueou desbloquear.</summary>
    /// <remarks>
    /// Os dois lados continuam lendo o histórico; enviar dá 422 <c>CONVERSATION_BLOCKED</c>. Devolve a
    /// conversa (<c>blocked</c>, <c>blockedByMe</c>), e os dois lados recebem
    /// <c>conversationBlockChanged</c> pelo hub. Idempotente. Já bloqueada pelo outro lado: 422
    /// <c>CONVERSATION_ALREADY_BLOCKED</c>. Não exige perfil ativo.
    /// </remarks>
    [HttpPut("{conversationId:guid}/block")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BlockAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await blockConversation.HandleAsync(
            new BlockConversationCommand(User.GetUserId(), CurrentRole(), conversationId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Desbloqueia a conversa. Só quem bloqueou.</summary>
    /// <remarks>
    /// Bloqueada pelo outro lado: 403 <c>CONVERSATION_UNBLOCK_FORBIDDEN</c>. Idempotente: conversa já
    /// liberada volta como está. Os dois lados recebem <c>conversationBlockChanged</c> pelo hub.
    /// </remarks>
    [HttpDelete("{conversationId:guid}/block")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ConversationResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await unblockConversation.HandleAsync(
            new UnblockConversationCommand(User.GetUserId(), CurrentRole(), conversationId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Denuncia uma mensagem recebida, para o admin analisar.</summary>
    /// <remarks>
    /// Corpo: <c>reason</c> (<c>spam</c>, <c>harassment</c>, <c>scam</c>, <c>inappropriate</c> ou
    /// <c>other</c>) e <c>description</c> (opcional, até 1.000 caracteres; obrigatória em
    /// <c>other</c>). Só quem recebeu: a própria mensagem dá 422 <c>CONVERSATION_REPORT_OWN_MESSAGE</c>.
    /// Uma denúncia por mensagem: 409 <c>CONVERSATION_REPORT_ALREADY_EXISTS</c>. Mensagem que não é
    /// desta conversa: 404 <c>CONVERSATION_MESSAGE_NOT_FOUND</c>. Não bloqueia a conversa (é o
    /// <c>PUT .../block</c>). Cada denúncia avisa o admin por e-mail: limite próprio
    /// (<c>reports</c>, o mesmo das avaliações), 429 <c>TOO_MANY_REQUESTS</c>.
    /// </remarks>
    [HttpPost("{conversationId:guid}/messages/{messageId:guid}/report")]
    [Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<SubmittedReportResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReportAsync(
        Guid conversationId,
        Guid messageId,
        [FromBody] ReportMessageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await reportMessage.HandleAsync(
            new ReportMessageCommand(User.GetUserId(), CurrentRole(), conversationId, messageId, request.Reason, request.Description),
            cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult($"{Route}/reports/{result.Value!.Id}")
            : result.ToActionResult();
    }

    /// <summary>O lado de quem pede: o anfitrião fala como anfitrião; o resto, como tutor.</summary>
    private string CurrentRole() => User.IsInRole(Roles.Host) ? Roles.Host : Roles.Owner;
}
