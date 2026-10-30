using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma conversa com mensagens esperando um dos lados há tempo bastante para o e-mail.</summary>
/// <param name="Recipient">O lado que não leu.</param>
/// <param name="RecipientProfileId">O tutor ou anfitrião que não leu.</param>
/// <param name="SenderProfileId">O outro lado, que mandou as mensagens.</param>
/// <param name="UnreadCount">Todas as mensagens do outro lado ainda não lidas na conversa.</param>
public sealed record UnreadNotificationRow(
    ConversationId ConversationId,
    ConversationParty Recipient,
    Guid RecipientProfileId,
    Guid SenderProfileId,
    int UnreadCount);
