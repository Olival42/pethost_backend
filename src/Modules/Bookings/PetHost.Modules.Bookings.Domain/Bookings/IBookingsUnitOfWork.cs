using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Confirma a transação do módulo Bookings.</summary>
public interface IBookingsUnitOfWork
{
    /// <summary>
    /// Grava tudo numa transação. Duas pessoas mexendo na mesma reserva ao mesmo tempo (o tutor
    /// cancela enquanto o anfitrião marca a chegada) voltam como <c>409 BOOKING_CONFLICT</c>, não
    /// como exceção (§15).
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Roda <paramref name="work"/> numa transação com o cantinho <paramref name="listingId"/>
    /// travado: dois aceites ao mesmo tempo no mesmo cantinho não passam juntos da capacidade.
    /// A conta de capacidade e a gravação precisam estar dentro de <paramref name="work"/>.
    /// </summary>
    Task<T> RunLockedOnListingAsync<T>(Guid listingId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
