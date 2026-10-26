using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Storage;

namespace PetHost.Modules.Conversations.Application.Messages.Responses;

/// <summary>Uma mensagem como sai na API e no hub. Sem <c>readAt</c>: o remetente não vê se foi lida.</summary>
/// <param name="SentBy"><c>owner</c> (tutor) ou <c>host</c> (anfitrião).</param>
/// <param name="Body">Texto ou legenda da foto. Omitido em foto sem legenda.</param>
/// <param name="ImageUrl">
/// Link da foto: URL assinada do bucket privado, que vence em <paramref name="ImageUrlExpiresAt"/>
/// (fotos antigas: URL pública, sem vencimento). Omitida em mensagem só de texto.
/// </param>
/// <param name="ImageUrlExpiresAt">
/// Quando o link da foto vence, em Unix time (segundos, UTC). Depois disso, peça outro em
/// <c>GET .../messages/{messageId}/photo</c>. Omitido sem foto ou em foto antiga.
/// </param>
/// <param name="BookingId">Reserva em andamento quando a mensagem foi enviada (histórico da reserva).</param>
public sealed record MessageResponse(
    Guid Id,
    Guid ConversationId,
    string SentBy,
    string? Body,
    string? ImageUrl,
    long? ImageUrlExpiresAt,
    Guid? BookingId,
    DateTimeOffset CreatedAt)
{
    /// <param name="photo">O link assinado da foto privada; sem ele, só a URL das fotos antigas sai.</param>
    public static MessageResponse From(MessageRow row, SignedImageUrl? photo = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new MessageResponse(
            row.Id.Value,
            row.ConversationId.Value,
            ConversationPartyValues.ToWire(row.SentBy),
            row.Body,
            photo?.Url ?? row.ImageUrl,
            photo?.ExpiresAt.ToUnixTimeSeconds(),
            row.BookingId,
            row.CreatedAt);
    }

    /// <param name="photo">O link assinado da foto privada; sem ele, só a URL das fotos antigas sai.</param>
    public static MessageResponse From(Message message, SignedImageUrl? photo = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessageResponse(
            message.Id.Value,
            message.ConversationId.Value,
            ConversationPartyValues.ToWire(message.SentBy),
            message.Body,
            photo?.Url ?? message.ImageUrl,
            photo?.ExpiresAt.ToUnixTimeSeconds(),
            message.BookingId,
            message.CreatedAt);
    }
}
