using FluentAssertions;
using Moq;
using PetHost.Modules.Bookings.Application.Bookings.ListBookings;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>A lista de reservas do admin: filtros que se somam, sem contato de nenhum lado.</summary>
public sealed class ListBookingsTests
{
    private readonly Mock<IBookingRepository> _repository = BookingsTestData.Repository();

    private ListBookingsQueryHandler Sut() => new(_repository.Object, BookingsTestData.ResponseBuilder());

    [Fact]
    public async Task HandleAsync_Should_PassEveryFilterToTheSearch_AndHideTheContacts()
    {
        // Arrange
        var confirmed = new BookingBuilder().BuildConfirmed();
        var ownerId = Guid.CreateVersion7();
        var hostId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();
        BookingSearch? search = null;
        _repository.Setup(r => r.SearchAsync(It.IsAny<BookingSearch>(), It.IsAny<CancellationToken>()))
            .Callback((BookingSearch s, CancellationToken _) => search = s)
            .ReturnsAsync(([confirmed], 1L));

        // Act
        var result = await Sut().HandleAsync(
            new ListBookingsQuery(["confirmed,requested"], ownerId, hostId, listingId, Page: 2, PageSize: 10),
            CancellationToken.None);

        // Assert
        search!.OwnerId.Should().Be(ownerId);
        search.HostId.Should().Be(hostId);
        search.ListingId.Should().Be(listingId);
        search.Statuses.Should().BeEquivalentTo([BookingStatus.Confirmed, BookingStatus.Requested]);
        search.Page.Should().Be(2);
        search.PageSize.Should().Be(10);
        var page = result.Value!;
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle(b => b.Id == confirmed.Id.Value);
        page.Items[0].Contact.Should().BeNull("o admin não vê o contato de nenhum lado");
    }

    [Fact]
    public async Task HandleAsync_Should_SearchEverything_When_ThereIsNoFilter()
    {
        // Arrange
        BookingSearch? search = null;
        _repository.Setup(r => r.SearchAsync(It.IsAny<BookingSearch>(), It.IsAny<CancellationToken>()))
            .Callback((BookingSearch s, CancellationToken _) => search = s)
            .ReturnsAsync(([], 0L));

        // Act
        var result = await Sut().HandleAsync(new ListBookingsQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        search!.OwnerId.Should().BeNull();
        search.HostId.Should().BeNull();
        search.ListingId.Should().BeNull();
        search.Statuses.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryError_When_FiltersAndPagingAreInvalid()
    {
        // Act
        var result = await Sut().HandleAsync(new ListBookingsQuery(["unknown"], Page: 0, PageSize: 500), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([BookingsErrors.StatusFilterInvalid, BookingsErrors.PageInvalid, BookingsErrors.PageSizeInvalid]);
        _repository.Verify(r => r.SearchAsync(It.IsAny<BookingSearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
