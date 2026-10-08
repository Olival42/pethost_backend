using System.Threading.Channels;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Caixa de saída em memória (uma fila por processo). Um e-mail ainda não enviado se perde se a API
/// reiniciar; para o "esqueci a senha" isso é aceitável — a pessoa pede de novo.
/// </summary>
internal sealed class EmailOutbox : IEmailOutbox
{
    /// <summary>
    /// Limite para a fila não crescer sem fim se o SMTP cair. Cheia, o enfileiramento
    /// espera — melhor segurar o request do que estourar a memória.
    /// </summary>
    private const int Capacity = 1_000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return _channel.Writer.WriteAsync(message, cancellationToken);
    }
}
