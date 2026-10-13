namespace PetHost.Modules.Payments.Application.Abstractions;

/// <summary>
/// Porta para os dois lados do pagamento: o tutor (Owners), o anfitrião (Hosts) e o nome e o
/// e-mail da conta de cada um (Auth). §5: a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IPaymentParties
{
    /// <summary>O id do tutor da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<Guid?> FindOwnerIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>O id do anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<Guid?> FindHostIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Nome e e-mail do tutor, para os avisos. <c>null</c> se ele sumiu.</summary>
    Task<PartyContact?> GetOwnerContactAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Nome e e-mail do anfitrião, para os avisos. <c>null</c> se ele sumiu.</summary>
    Task<PartyContact?> GetHostContactAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>A conta conectada do anfitrião no Stripe (<c>acct_...</c>), destino do repasse. <c>null</c> sem cadastro.</summary>
    Task<string?> GetHostPayoutAccountAsync(Guid hostId, CancellationToken cancellationToken);
}

/// <summary>Para quem vai um aviso. O e-mail nunca sai numa resposta da API.</summary>
public sealed record PartyContact(string Name, string Email);
