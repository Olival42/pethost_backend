using FluentAssertions;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>Qual nota cada cantinho mostra: a do cantinho (empresa) ou a do anfitrião (pessoa física).</summary>
public sealed class ListingRatingsTests
{
    private readonly Mock<IListingReviews> _reviews = new(MockBehavior.Strict);
    private readonly Listing _individual = new ListingBuilder().OwnedBy(ListingsTestData.IndividualHostId).Build();
    private readonly Listing _branch = new ListingBuilder().OwnedBy(ListingsTestData.CompanyHostId, isCompany: true).Build();

    private static Dictionary<Guid, ListingHostSummary> Hosts() => new()
    {
        [ListingsTestData.IndividualHostId] = ListingsTestData.Summary(ListingsTestData.IndividualHostId),
        [ListingsTestData.CompanyHostId] = ListingsTestData.Summary(ListingsTestData.CompanyHostId),
    };

    [Fact]
    public async Task ForAsync_Should_UseTheHostRatingForIndividualsAndTheListingRatingForCompanies_When_BothAreOnThePage()
    {
        // Arrange
        _reviews
            .Setup(r => r.GetHostRatingsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == ListingsTestData.IndividualHostId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ListingRating> { [ListingsTestData.IndividualHostId] = new(4.8m, 12) });
        _reviews
            .Setup(r => r.GetListingRatingsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == _branch.Id.Value), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ListingRating> { [_branch.Id.Value] = new(3.9m, 4) });

        // Act
        var ratings = await ListingRatings.ForAsync(_reviews.Object, [_individual, _branch], Hosts(), CancellationToken.None);

        // Assert
        ratings[_individual.Id.Value].Should().Be(new ListingRating(4.8m, 12));
        ratings[_branch.Id.Value].Should().Be(new ListingRating(3.9m, 4));
    }

    [Fact]
    public async Task ForAsync_Should_LeaveTheListingOut_When_NobodyReviewed()
    {
        // Arrange
        _reviews.Setup(r => r.GetHostRatingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, ListingRating>());

        // Act
        var ratings = await ListingRatings.ForAsync(_reviews.Object, [_individual], Hosts(), CancellationToken.None);

        // Assert
        ratings.Should().BeEmpty();
        _reviews.Verify(r => r.GetListingRatingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForAsync_Should_AskNothing_When_ThePageIsEmpty()
    {
        // Act
        var ratings = await ListingRatings.ForAsync(_reviews.Object, [], Hosts(), CancellationToken.None);

        // Assert
        ratings.Should().BeEmpty();
    }

    [Fact]
    public async Task ForAsync_Should_Throw_When_AnArgumentIsNull()
    {
        // Act & Assert
        await ((Func<Task>)(() => ListingRatings.ForAsync(null!, [], Hosts(), CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => ListingRatings.ForAsync(_reviews.Object, null!, Hosts(), CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => ListingRatings.ForAsync(_reviews.Object, [], null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ListingRatingResponse_Should_RoundToOneDecimalOrBeNull_When_Mapped()
    {
        // Act & Assert
        ListingRatingResponse.From(new ListingRating(4.75m, 8)).Should().Be(new ListingRatingResponse(4.8m, 8));
        ListingRatingResponse.From(null).Should().BeNull();
    }
}
