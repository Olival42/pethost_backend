using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>Confirma a transação do módulo Reviews.</summary>
public interface IReviewsUnitOfWork
{
    /// <summary>
    /// Grava tudo numa transação. A corrida que o banco barra volta como erro, não como exceção
    /// (§15): duas avaliações da mesma reserva ao mesmo tempo dão <c>409 REVIEW_ALREADY_EXISTS</c>;
    /// a avaliação alterada por outro request ao mesmo tempo dá <c>409 REVIEW_CONFLICT</c>.
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
