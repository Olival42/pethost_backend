using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Storage;

namespace PetHost.Modules.Conversations.Application.Messages.Responses;

/// <summary>
/// Monta a <see cref="MessageResponse"/> com o link da foto: as fotos do chat ficam no bucket
/// <b>privado</b>, e quem lê a mensagem recebe uma URL assinada que vence (<c>imageUrlExpiresAt</c>).
/// </summary>
/// <remarks>
/// Só use depois de conferir que quem pede é da conversa (ou o admin, na moderação): quem tem a
/// URL abre a foto sem token até ela vencer. Assinar é conta local, sem ida ao bucket, então uma
/// página inteira de mensagens não custa uma consulta por foto.
/// </remarks>
public sealed class MessagePhotoLinks(IPrivateImageStorage storage)
{
    public async Task<MessageResponse> ToResponseAsync(MessageRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        return MessageResponse.From(row, await SignAsync(row.ImageKey, cancellationToken).ConfigureAwait(false));
    }

    public async Task<MessageResponse> ToResponseAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return MessageResponse.From(message, await SignAsync(message.ImageKey, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<MessageResponse>> ToResponsesAsync(IEnumerable<MessageRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var responses = new List<MessageResponse>();
        foreach (var row in rows)
            responses.Add(await ToResponseAsync(row, cancellationToken).ConfigureAwait(false));

        return responses;
    }

    private async Task<SignedImageUrl?> SignAsync(string? key, CancellationToken cancellationToken) =>
        key is null ? null : await storage.SignUrlAsync(key, cancellationToken).ConfigureAwait(false);
}
