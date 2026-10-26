using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma mensagem como a leitura projeta. Sem <c>read_at</c>: o remetente não vê se foi lida.</summary>
/// <param name="ImageUrl">URL pública da foto (só fotos antigas).</param>
/// <param name="ImageKey">Chave da foto no bucket privado.</param>
/// <param name="BookingId">Reserva em andamento da dupla no envio, ou <c>null</c>.</param>
public sealed record MessageRow(
    MessageId Id,
    ConversationId ConversationId,
    ConversationParty SentBy,
    string? Body,
    string? ImageUrl,
    string? ImageKey,
    Guid? BookingId,
    DateTimeOffset CreatedAt);
