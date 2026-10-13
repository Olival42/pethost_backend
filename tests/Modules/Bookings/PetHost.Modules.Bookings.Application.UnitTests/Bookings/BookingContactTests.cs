using FluentAssertions;
using PetHost.Modules.Bookings.Application.Bookings.GetBookingById;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>Endereço exato e telefone só depois da confirmação (escopo, regra 7).</summary>
public sealed class BookingContactTests
{
    private static async Task<BookingResponse> GetAsync(Booking booking, Guid userId, string role)
    {
        var sut = new GetBookingByIdQueryHandler(
            BookingsTestData.Repository(booking).Object,
            BookingsTestData.Parties().Object,
            BookingsTestData.ResponseBuilder());

        var result = await sut.HandleAsync(new GetBookingByIdQuery(userId, role, booking.Id.Value), CancellationToken.None);

        return result.Value!;
    }

    [Fact]
    public async Task Contact_Should_GiveTheOwnerTheHostPhoneAndExactAddress_When_Confirmed()
    {
        // Act
        var response = await GetAsync(new BookingBuilder().BuildConfirmed(), BookingsTestData.OwnerUserId, Roles.Owner);

        // Assert
        response.Contact.Should().Be(new BookingContactResponse(BookingsTestData.HostPhone, BookingsTestData.ListingAddress));
    }

    [Fact]
    public async Task Contact_Should_GiveTheHostOnlyTheOwnerPhone_When_InProgress()
    {
        // Act
        var response = await GetAsync(new BookingBuilder().BuildInProgress(), BookingsTestData.HostUserId, Roles.Host);

        // Assert
        response.Contact.Should().Be(new BookingContactResponse(BookingsTestData.OwnerPhone, null));
    }

    [Fact]
    public async Task Contact_Should_BeHidden_When_StillRequested()
    {
        // Act
        var response = await GetAsync(new BookingBuilder().Build(), BookingsTestData.OwnerUserId, Roles.Owner);

        // Assert
        response.Contact.Should().BeNull();
    }

    [Fact]
    public async Task Contact_Should_BeHidden_When_Cancelled()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        booking.Cancel(BookingParty.Owner, null, BookingBuilder.DefaultNow.AddDays(1));

        // Act
        var response = await GetAsync(booking, BookingsTestData.OwnerUserId, Roles.Owner);

        // Assert
        response.Contact.Should().BeNull();
    }

    [Fact]
    public async Task Contact_Should_BeHidden_When_ViewerIsAdmin()
    {
        // Act
        var response = await GetAsync(new BookingBuilder().BuildConfirmed(), Guid.CreateVersion7(), Roles.Admin);

        // Assert
        response.Contact.Should().BeNull();
    }

    [Fact]
    public async Task Contact_Should_StayAvailable_When_StayIsCompleted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();
        booking.CheckOut(BookingClock.StartOf(BookingBuilder.DefaultCheckOut).AddHours(10));

        // Act
        var response = await GetAsync(booking, BookingsTestData.OwnerUserId, Roles.Owner);

        // Assert
        response.Contact!.Phone.Should().Be(BookingsTestData.HostPhone);
    }
}
