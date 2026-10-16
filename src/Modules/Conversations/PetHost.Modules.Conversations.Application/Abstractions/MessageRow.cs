using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma mensagem como a leitura projeta. Sem <c>read_at</c>: o remetente não vê se foi lida.</summary>
/// <param name="BookingId">Reserva em andamento da dupla no envio, ou <c>null</c>.</param>
public sealed record MessageRow(
    MessageId Id,
    ConversationId ConversationId,
    ConversationParty SentBy,
    string? Body,
    string? ImageUrl,
    Guid? BookingId,
    DateTimeOffset CreatedAt);
