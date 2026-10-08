namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Caixa de saída dos e-mails. Quem quer mandar e-mail deixa aqui e segue;
/// o envio acontece em segundo plano no <see cref="EmailDispatcher"/>.
/// </summary>
/// <remarks>
/// Enfileirar em vez de mandar na hora tem dois motivos: o request não fica preso
/// ao SMTP (lento, às vezes fora do ar) e o tempo de resposta não muda conforme
/// houve envio ou não — o que, no "esqueci a senha", revelaria quem tem conta.
/// </remarks>
public interface IEmailOutbox
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken);
}
