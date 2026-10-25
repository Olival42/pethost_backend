using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.IntegrationTests.Bookings;

/// <summary>
/// O Unit of Work contra o Postgres: tutor e anfitrião mexendo na mesma reserva ao mesmo tempo. A
/// concorrência otimista (<c>xmin</c>) faz quem grava depois receber <c>BOOKING_CONFLICT</c>, sem
/// perder a gravação de quem chegou antes.
/// </summary>
[Collection(BookingsApiCollection.Name)]
public sealed class BookingsUnitOfWorkTests(BookingsApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChangesAsync_Should_ReturnConflict_When_TheOwnerAndTheHostChangeTheSameBookingAtTheSameTime()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        await using (var setup = fixture.Services.CreateAsyncScope())
        {
            setup.ServiceProvider.GetRequiredService<IBookingRepository>().Add(booking);
            (await setup.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>().SaveChangesAsync(Ct)).IsSuccess.Should().BeTrue();
        }

        await using var owner = fixture.Services.CreateAsyncScope();
        await using var host = fixture.Services.CreateAsyncScope();
        var ownersCopy = await owner.ServiceProvider.GetRequiredService<IBookingRepository>().GetByIdAsync(booking.Id, Ct);
        var hostsCopy = await host.ServiceProvider.GetRequiredService<IBookingRepository>().GetByIdAsync(booking.Id, Ct);
        var now = BookingBuilder.DefaultNow.AddHours(1);

        hostsCopy!.Decline("Não recebo nesse período", now).IsSuccess.Should().BeTrue();
        (await host.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>().SaveChangesAsync(Ct)).IsSuccess.Should().BeTrue();

        // Act
        ownersCopy!.Cancel(BookingParty.Owner, "Mudei de planos", now).IsSuccess.Should().BeTrue();
        var result = await owner.ServiceProvider.GetRequiredService<IBookingsUnitOfWork>().SaveChangesAsync(Ct);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        await using var check = fixture.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<IBookingRepository>().GetByIdAsync(booking.Id, Ct);
        stored!.Status.Should().Be(BookingStatus.Declined, "a primeira gravação vale");
    }
}
