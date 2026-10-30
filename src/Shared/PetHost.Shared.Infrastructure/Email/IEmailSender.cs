namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Entrega um e-mail de verdade. Só o <see cref="EmailDispatcher"/> chama.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
