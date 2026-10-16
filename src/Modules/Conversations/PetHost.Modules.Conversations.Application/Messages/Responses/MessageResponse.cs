using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Application.Messages.Responses;

/// <summary>Uma mensagem como sai na API e no hub. Sem <c>readAt</c>: o remetente não vê se foi lida.</summary>
/// <param name="SentBy"><c>owner</c> (tutor) ou <c>host</c> (anfitrião).</param>
/// <param name="Body">Texto ou legenda da foto. Omitido em foto sem legenda.</param>
/// <param name="ImageUrl">Foto no bucket público. Omitida em mensagem só de texto.</param>
/// <param name="BookingId">Reserva em andamento quando a mensagem foi enviada (histórico da reserva).</param>
public sealed record MessageResponse(
    Guid Id,
    Guid ConversationId,
    string SentBy,
    string? Body,
    string? ImageUrl,
    Guid? BookingId,
    DateTimeOffset CreatedAt)
{
    public static MessageResponse From(MessageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new MessageResponse(
            row.Id.Value,
            row.ConversationId.Value,
            ConversationPartyValues.ToWire(row.SentBy),
            row.Body,
            row.ImageUrl,
            row.BookingId,
            row.CreatedAt);
    }

    public static MessageResponse From(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessageResponse(
            message.Id.Value,
            message.ConversationId.Value,
            ConversationPartyValues.ToWire(message.SentBy),
            message.Body,
            message.ImageUrl,
            message.BookingId,
            message.CreatedAt);
    }
}
