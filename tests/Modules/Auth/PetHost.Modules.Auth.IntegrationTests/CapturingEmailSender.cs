using System.Collections.Concurrent;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Auth.IntegrationTests;

/// <summary>
/// Substitui o SMTP nos testes: guarda cada e-mail em vez de entregar. A fila e o
/// despachante em segundo plano continuam os de verdade.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear() => _sent.Clear();

    /// <summary>
    /// Espera o despachante entregar <paramref name="count"/> e-mails para o endereço.
    /// O envio é assíncrono, então o e-mail chega um pouco depois da resposta HTTP.
    /// </summary>
    public async Task<IReadOnlyList<EmailMessage>> WaitForAsync(
        string toAddress,
        int count,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (true)
        {
            var messages = _sent.Where(m => m.ToAddress == toAddress).ToList();
            if (messages.Count >= count || DateTime.UtcNow > deadline)
                return messages;

            await Task.Delay(25, cancellationToken);
        }
    }
}
