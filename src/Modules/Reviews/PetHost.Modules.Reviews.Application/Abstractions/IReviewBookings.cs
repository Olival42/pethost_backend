using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Abstractions;

/// <summary>Porta para as reservas avaliadas (módulo Bookings).</summary>
public interface IReviewBookings
{
    /// <summary>A estadia da reserva <paramref name="bookingId"/>, ou <c>null</c> se ela não existe.</summary>
    Task<ReviewedStay?> GetStayAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>
    /// Estadias concluídas com check-out entre <paramref name="from"/> e <paramref name="to"/>, da
    /// mais antiga para a mais nova, no máximo <paramref name="limit"/>. Base do lembrete.
    /// </summary>
    Task<IReadOnlyList<ReviewedStay>> ListCompletedAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken);
}
