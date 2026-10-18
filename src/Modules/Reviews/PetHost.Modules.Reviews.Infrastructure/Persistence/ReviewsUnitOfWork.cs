using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence;

/// <summary>
/// Confirma a transação do módulo Reviews e traduz as corridas que o banco barra em erro de negócio
/// (§15): os índices únicos (avaliação por lado, denúncia por conta, lembrete por lado) e a
/// concorrência otimista (<c>xmin</c>).
/// </summary>
/// <remarks>
/// Em falha, o rastreamento é limpo: as mudanças recusadas não voltam a ser tentadas por um próximo
/// <c>SaveChanges</c> no mesmo escopo.
/// </remarks>
internal sealed class ReviewsUnitOfWork(ReviewsDbContext dbContext) : IReviewsUnitOfWork
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

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
            return Result.Failure(ReviewsErrors.Conflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation } postgres)
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(ErrorFor(postgres.ConstraintName));
        }
    }

    private static Error ErrorFor(string? constraint) => constraint switch
    {
        "uq_reviews_booking_id_author_side" => ReviewsErrors.AlreadyExists,
        "uq_review_reports_review_id_reported_by" => ReviewsErrors.ReportAlreadyExists,
        _ => ReviewsErrors.Conflict,
    };
}
