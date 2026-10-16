using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Conversations.Application.Conversations.LiftModerationBlock;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>
/// O admin sobre uma conversa: hoje, só tirar o bloqueio da moderação. A fila de denúncias fica em
/// <c>ConversationReportsAdminController</c>. Tudo só admin, e vai para a trilha.
/// </summary>
[ApiController]
[Route("api/v1/conversations")]
[Authorize(Roles = Roles.Admin)]
public sealed class ConversationsAdminController(
    ICommandHandler<LiftModerationBlockCommand, ConversationBlockResponse> liftModerationBlock) : ControllerBase
{
    /// <summary>Tira o bloqueio da moderação (denúncia procedente) de uma conversa.</summary>
    /// <remarks>
    /// Para um procedente dado por engano ou reconsiderado. O bloqueio de um dos lados, se houver,
    /// continua: <c>blocked</c> na resposta diz se a conversa ainda está bloqueada. Os dois lados
    /// recebem <c>conversationBlockChanged</c> pelo hub. Idempotente. A denúncia continua procedente.
    /// Conversa inexistente: 404 <c>CONVERSATION_NOT_FOUND</c>.
    /// </remarks>
    [HttpDelete("{conversationId:guid}/moderation-block")]
    [ProducesResponseType<ApiResponse<ConversationBlockResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ConversationBlockResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ConversationBlockResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ConversationBlockResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LiftModerationBlockAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await liftModerationBlock.HandleAsync(
            new LiftModerationBlockCommand(User.GetUserId(), conversationId),
            cancellationToken);

        return result.ToActionResult();
    }
}
