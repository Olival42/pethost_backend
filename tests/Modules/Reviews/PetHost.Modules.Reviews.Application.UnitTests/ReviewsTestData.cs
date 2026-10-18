using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.TestKit;

namespace PetHost.Modules.Reviews.Application.UnitTests;

/// <summary>Dados comuns dos testes da Application: a Camila (tutora), a Dona Cida (anfitriã) e a Pipoca.</summary>
internal static class ReviewsTestData
{
    public static readonly ReviewerProfile AvailableOwner = new(ReviewBuilder.OwnerId, IsAvailable: true);
    public static readonly ReviewerProfile UnavailableOwner = new(ReviewBuilder.OwnerId, IsAvailable: false);
    public static readonly ReviewerProfile AvailableHost = new(ReviewBuilder.HostId, IsAvailable: true);
    public static readonly ReviewerProfile UnavailableHost = new(ReviewBuilder.HostId, IsAvailable: false);

    public static readonly ReviewContact OwnerContact = new(ReviewBuilder.OwnerId, "Camila", "camila@exemplo.com", IsReachable: true);
    public static readonly ReviewContact HostContact = new(ReviewBuilder.HostId, "Cida", "cida@exemplo.com", IsReachable: true);

    public static ReviewRow Row(Review review) => ReviewResponseBuilder.ToRow(review);

    /// <summary>
    /// As duas pessoas e a Pipoca, com tudo encontrado e disponível. Estrito: chamada não
    /// configurada falha. O teste troca o que importa.
    /// </summary>
    public static Mock<IReviewParties> Parties()
    {
        var parties = new Mock<IReviewParties>(MockBehavior.Strict);

        parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(AvailableOwner);
        parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(AvailableHost);

        parties
            .Setup(p => p.GetInfoAsync(ReviewParty.Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewPartyInfo> { [ReviewBuilder.OwnerId] = new(ReviewBuilder.OwnerId, "Camila", "https://img.pethost.test/camila.png") });

        parties
            .Setup(p => p.GetInfoAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewPartyInfo> { [ReviewBuilder.HostId] = new(ReviewBuilder.HostId, "Cida", null) });

        parties
            .Setup(p => p.GetPetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [ReviewBuilder.PetId] = "Pipoca" });

        parties
            .Setup(p => p.GetContactsAsync(ReviewParty.Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewContact> { [ReviewBuilder.OwnerId] = OwnerContact });

        parties
            .Setup(p => p.GetContactsAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewContact> { [ReviewBuilder.HostId] = HostContact });

        return parties;
    }
}
