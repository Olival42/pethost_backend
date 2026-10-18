using PetHost.Modules.Reviews.Application.Reviews.Responses;

namespace PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;

/// <summary>Uma nota do histórico do pet: de qual estadia, quem deu, a nota e o comentário.</summary>
/// <param name="ReviewId">A avaliação do anfitrião onde a nota está.</param>
/// <param name="HostId">O anfitrião que deu a nota (<c>host.hosts.id</c>).</param>
/// <param name="ListingId">O cantinho da estadia.</param>
/// <param name="Host">Nome curto e foto do anfitrião. Nulo se a conta sumiu.</param>
/// <param name="StayEndedAt">Quando a estadia acabou.</param>
public sealed record PetReviewResponse(
    Guid ReviewId,
    Guid HostId,
    Guid ListingId,
    ReviewAuthorSummary? Host,
    int Rating,
    string? Comment,
    DateTimeOffset StayEndedAt);
