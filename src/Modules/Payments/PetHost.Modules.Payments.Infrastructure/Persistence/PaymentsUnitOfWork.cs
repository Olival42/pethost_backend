using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Infrastructure.Persistence;

/// <summary>
/// Confirma a transação do módulo Payments e traduz as corridas que o banco barra em erro de
/// negócio (§15): concorrência otimista (<c>xmin</c>) e dois pagamentos criados para a mesma
/// reserva ao mesmo tempo (<c>uq_payments_booking_id</c>) → <c>409 PAYMENT_CONFLICT</c>.
/// </summary>
internal sealed class PaymentsUnitOfWork(PaymentsDbContext dbContext) : IPaymentsUnitOfWork
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
            return Result.Failure(PaymentsErrors.Conflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(PaymentsErrors.Conflict);
        }
    }
}
