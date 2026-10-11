using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PetHost.Shared.Infrastructure.Email.Persistence;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Caixa de saída persistente: cada e-mail vira uma linha em <c>email.outbox_emails</c> antes
/// de o request seguir. Se a API reiniciar ou o SMTP estiver fora, o e-mail continua lá e o
/// <see cref="EmailDispatcher"/> entrega quando der.
/// </summary>
/// <remarks>
/// Grava num contexto próprio, fora da transação do módulo que pediu o envio: o e-mail é
/// gravado depois que o caso de uso já salvou o que tinha de salvar.
/// </remarks>
internal sealed class EmailOutbox(
    IDbContextFactory<EmailDbContext> contextFactory,
    TimeProvider timeProvider) : IEmailOutbox
{
    /// <summary>
    /// Aviso ao despachante de que chegou e-mail, para não esperar a próxima varredura.
    /// Um aviso pendente basta: o despachante sempre esvazia tudo o que está na vez.
    /// </summary>
    private readonly Channel<bool> _wakeUp = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<bool> WakeUp => _wakeUp.Reader;

    public async ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            context.Outbox.Add(OutboxEmail.From(message, Guid.CreateVersion7(), timeProvider.GetUtcNow()));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _wakeUp.Writer.TryWrite(true);
    }
}
