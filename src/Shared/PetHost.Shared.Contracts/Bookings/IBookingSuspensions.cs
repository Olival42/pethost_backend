namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// Contrato do módulo Bookings para a suspensão pelo admin (§5). O Owners e o Hosts chamam isto
/// depois de gravar a suspensão; o Bookings cancela pela plataforma as reservas que ainda não
/// começaram (solicitada, aceita, confirmada), avisa o pagamento e manda e-mail aos dois lados.
/// </summary>
/// <remarks>
/// Idempotente: suspender de novo cancela o que tiver sobrado. A estadia em andamento continua
/// (o pet está com o anfitrião) — o check-out encerra.
/// </remarks>
public interface IBookingSuspensions
{
    /// <summary>
    /// O tutor <paramref name="ownerId"/> (<c>owner.owners.id</c>) foi suspenso: as reservas dele são
    /// canceladas; a paga fica sem reembolso (o que ele pagou segue o 90/10 para o anfitrião).
    /// </summary>
    /// <returns>Quantas reservas foram canceladas agora.</returns>
    Task<int> CancelForSuspendedOwnerAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// O anfitrião <paramref name="hostId"/> (<c>host.hosts.id</c>) foi suspenso: as reservas dos
    /// cantinhos dele são canceladas; a paga devolve 100% ao tutor.
    /// </summary>
    /// <returns>Quantas reservas foram canceladas agora.</returns>
    Task<int> CancelForSuspendedHostAsync(Guid hostId, CancellationToken cancellationToken);
}
