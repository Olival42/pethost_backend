namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>
/// Porta para as notas dos tutores (módulo Reviews): o anfitrião vê a nota de quem pede a estadia.
/// §5: a Application declara a interface; a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IBookingReviews
{
    /// <summary>Nota de cada tutor, do que os anfitriões escreveram dele. Sem avaliação, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, OwnerRating>> GetOwnerRatingsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken);
}

/// <summary>A nota média do tutor (1 a 5) e quantas avaliações a compõem.</summary>
public sealed record OwnerRating(decimal Average, int Count);
