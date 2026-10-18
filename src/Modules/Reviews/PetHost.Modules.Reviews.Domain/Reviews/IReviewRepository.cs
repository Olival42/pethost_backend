namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>Acesso a <see cref="Review"/>. A Application só conhece esta interface (§11).</summary>
public interface IReviewRepository
{
    /// <summary>Pelo id, se não foi apagada (oculta vem). Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken);

    /// <summary>
    /// O lado <paramref name="side"/> já avaliou a reserva, inclusive com avaliação apagada? Uma por
    /// lado: a apagada não libera uma nova.
    /// </summary>
    Task<bool> ExistsForBookingAsync(Guid bookingId, ReviewParty side, CancellationToken cancellationToken);

    /// <summary>Os lados que já avaliaram (inclusive apagada) cada reserva de <paramref name="bookingIds"/>.</summary>
    Task<IReadOnlySet<(Guid BookingId, ReviewParty Side)>> GetReviewedSidesAsync(
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// A avaliação que o lado <paramref name="side"/> fez da reserva, se não foi apagada. Rastreada:
    /// a publicação às cegas publica as duas juntas.
    /// </summary>
    Task<Review?> FindByBookingAsync(Guid bookingId, ReviewParty side, CancellationToken cancellationToken);

    /// <summary>
    /// Avaliações já publicadas em <paramref name="now"/> cujo avaliado ainda não recebeu o aviso por
    /// e-mail (sem apagadas nem ocultas), da mais antiga para a mais nova, no máximo
    /// <paramref name="take"/>. Rastreadas.
    /// </summary>
    Task<IReadOnlyList<Review>> ListPublishedToNotifyAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    void Add(Review review);
}
