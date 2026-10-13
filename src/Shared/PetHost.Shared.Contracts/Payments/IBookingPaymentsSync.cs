namespace PetHost.Shared.Contracts.Payments;

/// <summary>
/// Contrato do módulo Payments para ser avisado de que uma reserva mudou (§5): cancelada,
/// expirada ou concluída. O aviso é só um aviso — o Payments relê a reserva e faz o que falta
/// (reembolso, cancelar cobranças em aberto, repasse ao anfitrião).
/// </summary>
/// <remarks>
/// O Bookings chama depois de gravar. Se o aviso falhar, a manutenção periódica do Payments
/// reconcilia do mesmo jeito: nada se perde, só atrasa.
/// </remarks>
public interface IBookingPaymentsSync
{
    Task SyncBookingAsync(Guid bookingId, CancellationToken cancellationToken);
}
