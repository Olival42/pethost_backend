using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Conversations.Infrastructure.UnreadEmail;

/// <summary>
/// O resumo de mensagens não lidas por e-mail, pela caixa de saída (<see cref="IEmailOutbox"/>), em
/// segundo plano. Só quem mandou e quantas: o texto e as fotos ficam no app.
/// </summary>
internal sealed class UnreadMessagesEmailNotifier(IEmailOutbox outbox, IOptions<UnreadEmailOptions> options) : IUnreadMessagesNotifier
{
    public const string Subject = "PetHost — você tem mensagens esperando";

    public async Task SendAsync(UnreadDigest digest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var lines = digest.Items.Select(item => string.Create(
            CultureInfo.InvariantCulture,
            $"{item.UnreadCount} {(item.UnreadCount == 1 ? "mensagem" : "mensagens")} de {item.SenderName}"));

        var appUrl = options.Value.AppUrl?.Trim();
        var call = string.IsNullOrEmpty(appUrl)
            ? "Abra o app do PetHost para ler e responder."
            : $"Abra o PetHost para ler e responder: {appUrl}";

        string[] paragraphs =
        [
            $"Olá, {digest.Name}.",
            "Chegaram mensagens no chat do PetHost que você ainda não leu:",
            .. lines.Select(line => $"• {line}"),
            call,
            "Você recebe este aviso quando uma mensagem fica sem ler por um tempo. Abrir a conversa no app já basta.",
        ];

        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        await outbox
            .EnqueueAsync(new EmailMessage(digest.Email, digest.Name, Subject, text, html,
                new EmailAction("Ler minhas mensagens", string.IsNullOrEmpty(appUrl) ? "/conversations" : appUrl)), cancellationToken)
            .ConfigureAwait(false);
    }
}
