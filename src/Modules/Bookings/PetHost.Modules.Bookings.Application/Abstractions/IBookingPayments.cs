namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>
/// Porta para o pagamento das reservas (módulo Payments). §5: a Application declara a interface;
/// a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IBookingPayments
{
    /// <summary>O tutor <paramref name="ownerId"/> tem parcela em atraso? Assim não pede estadia nova.</summary>
    Task<bool> HasOverduePaymentAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Avisa o pagamento que a reserva mudou (cancelada, expirada ou concluída), para ele devolver o
    /// dinheiro, cancelar as cobranças em aberto ou repassar ao anfitrião. Chamado depois de gravar;
    /// <b>nunca falha</b> o request — se o aviso não passar, a manutenção do pagamento reconcilia depois.
    /// </summary>
    Task BookingChangedAsync(Guid bookingId, CancellationToken cancellationToken);
}
