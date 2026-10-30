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
    private int _attempts;

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    /// <summary>Quantas vezes o despachante tentou enviar (com sucesso ou não).</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>Quando preenchido, todo envio falha com esta exceção — o SMTP "fora do ar".</summary>
    public Exception? Failure { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);

        if (Failure is not null)
            throw Failure;

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _sent.Clear();
        Failure = null;
        Volatile.Write(ref _attempts, 0);
    }

    /// <summary>
    /// Espera o despachante entregar <paramref name="count"/> e-mails para o endereço.
    /// O envio é assíncrono, então o e-mail chega um pouco depois da resposta HTTP.
    /// </summary>
    /// <param name="timeout">Quanto esperar no máximo (padrão 5 s).</param>
    public async Task<IReadOnlyList<EmailMessage>> WaitForAsync(
        string toAddress,
        int count,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (true)
        {
            var messages = _sent.Where(m => m.ToAddress == toAddress).ToList();
            if (messages.Count >= count || DateTime.UtcNow > deadline)
                return messages;

            await Task.Delay(25, cancellationToken);
        }
    }
}
