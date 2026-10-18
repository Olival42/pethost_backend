namespace PetHost.Shared.Contracts.Reviews;

/// <summary>
/// Contrato público de leitura do módulo Reviews (§5). O Listings pergunta por aqui a nota dos
/// anfitriões e dos cantinhos (o cartão da busca, o detalhe e o selo "Bem avaliado"); o Bookings, a
/// nota dos tutores (no pedido de reserva, para o anfitrião) — sem referenciar o Reviews nem fazer
/// JOIN entre schemas.
/// </summary>
/// <remarks>
/// Só contam as avaliações publicadas (às cegas: os dois lados avaliaram ou o prazo acabou), sem as
/// apagadas pelo autor e sem as ocultadas pelo admin.
/// </remarks>
public interface IReviewDirectory
{
    /// <summary>Nota de cada anfitrião (todas as filiais, se for empresa). Sem avaliação, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetHostRatingsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);

    /// <summary>Nota de cada cantinho (a filial, se for empresa). Sem avaliação, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetListingRatingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Nota de cada tutor (<c>owner.owners.id</c>), do que os anfitriões escreveram dele. Sem
    /// avaliação, não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RatingSummary>> GetOwnerRatingsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken);
}

/// <summary>A nota média e quantas avaliações a compõem.</summary>
/// <param name="Average">Média das notas (1 a 5), com 2 casas.</param>
/// <param name="Count">Quantas avaliações.</param>
public sealed record RatingSummary(decimal Average, int Count);
