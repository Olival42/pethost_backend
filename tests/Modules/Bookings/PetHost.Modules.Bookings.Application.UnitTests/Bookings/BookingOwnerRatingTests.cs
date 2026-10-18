using FluentAssertions;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>A nota do tutor no pedido: o anfitrião (e o admin) veem; o próprio tutor, não.</summary>
public sealed class BookingOwnerRatingTests
{
    private readonly Booking _booking = new BookingBuilder().Build();

    [Theory]
    [InlineData(BookingParty.Host)]
    [InlineData(null)]
    public async Task BuildAsync_Should_ShowTheOwnerRatingRounded_When_TheHostOrTheAdminReads(BookingParty? viewer)
    {
        // Act
        var response = await BookingsTestData.ResponseBuilder().BuildAsync(_booking, viewer, CancellationToken.None);

        // Assert
        response.OwnerRating.Should().Be(new BookingOwnerRatingResponse(4.8m, 4));
    }

    [Fact]
    public async Task BuildAsync_Should_LeaveTheRatingOutWithoutAskingReviews_When_TheOwnerReads()
    {
        // Arrange
        var reviews = BookingsTestData.Reviews();
        var sut = new BookingResponseBuilder(BookingsTestData.Listings().Object, BookingsTestData.Parties().Object, BookingsTestData.Pets().Object, reviews.Object);

        // Act
        var response = await sut.BuildAsync(_booking, BookingParty.Owner, CancellationToken.None);

        // Assert
        response.OwnerRating.Should().BeNull();
        reviews.Verify(r => r.GetOwnerRatingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_Should_LeaveTheRatingOut_When_NobodyReviewedTheOwner()
    {
        // Arrange
        var reviews = new Mock<IBookingReviews>();
        reviews
            .Setup(r => r.GetOwnerRatingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, OwnerRating>());
        var sut = new BookingResponseBuilder(BookingsTestData.Listings().Object, BookingsTestData.Parties().Object, BookingsTestData.Pets().Object, reviews.Object);

        // Act
        var response = await sut.BuildAsync(_booking, BookingParty.Host, CancellationToken.None);

        // Assert
        response.OwnerRating.Should().BeNull();
    }
}
