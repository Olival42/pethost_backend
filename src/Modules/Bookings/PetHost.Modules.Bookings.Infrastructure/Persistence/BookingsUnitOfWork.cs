using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence;

/// <summary>
/// Confirma a transação do módulo Bookings e traduz a corrida que o banco barra em erro de
/// negócio (§15): concorrência otimista (<c>xmin</c>) → <c>409 BOOKING_CONFLICT</c>.
/// </summary>
internal sealed class BookingsUnitOfWork(BookingsDbContext dbContext) : IBookingsUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(BookingsErrors.Conflict);
        }
    }

    /// <summary>
    /// Trava do cantinho: <c>pg_advisory_xact_lock</c> com uma chave de 64 bits tirada do id. A
    /// trava vale até o fim da transação; quem chega depois espera e já lê o que o primeiro gravou.
    /// </summary>
    public async Task<T> RunLockedOnListingAsync<T>(
        Guid listingId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var key = $"booking:listing:{listingId}";
        await dbContext.Database
            .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken)
            .ConfigureAwait(false);

        var result = await work(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }
}
