using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Messages.SendMessage;

/// <summary>O tutor ou o anfitrião envia uma mensagem de texto na conversa.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c> — o lado de quem envia.</param>
/// <param name="ConversationId">Vem da rota.</param>
/// <param name="Body">O texto, até 2.000 caracteres (sem contar os espaços nas pontas).</param>
public sealed record SendMessageCommand(Guid UserId, string Role, Guid ConversationId, string? Body) : ICommand<MessageResponse>;
