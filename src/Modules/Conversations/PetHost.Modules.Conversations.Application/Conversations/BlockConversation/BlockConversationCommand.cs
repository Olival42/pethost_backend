using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.BlockConversation;

/// <summary>O tutor ou o anfitrião bloqueia a conversa: ninguém envia até quem bloqueou desbloquear.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c> — o lado de quem bloqueia.</param>
/// <param name="ConversationId">Vem da rota.</param>
public sealed record BlockConversationCommand(Guid UserId, string Role, Guid ConversationId) : ICommand<ConversationResponse>;
