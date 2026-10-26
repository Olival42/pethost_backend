using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;

/// <summary>O tutor ou o anfitrião envia uma foto na conversa, com legenda opcional.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c> — o lado de quem envia.</param>
/// <param name="ConversationId">Vem da rota.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
/// <param name="Body">A parte <c>body</c> do multipart: legenda opcional, até 2.000 caracteres.</param>
/// <param name="MessageId">
/// Id da mensagem a criar (o upload direto usa o id do upload). Se já existe nesta conversa, a
/// mensagem existente volta, sem gravar nem avisar de novo: a confirmação repetida não duplica.
/// </param>
public sealed record SendPhotoMessageCommand(
    Guid UserId,
    string Role,
    Guid ConversationId,
    ImageUpload? Image,
    string? Body,
    Guid? MessageId = null) : ICommand<MessageResponse>;
