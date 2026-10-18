using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.GetMyDashboard;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Contracts.Payments;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.Hosts;

/// <summary>Painel do anfitrião: reservas do Bookings e ganhos do Payments numa resposta só.</summary>
public sealed class HostDashboardTests
{
    private static readonly HostBookingsOverview EmptyOverview = new(0, [], 0, 0, [], 0, [], 0, []);
    private static readonly HostEarningsSummary NoEarnings =
        new(0, 0, 0, 0, new HostEarningsBreakdown(0, 0, 0, 0), new HostHeldPayouts(0, 0, 0), new HostExpectedPayouts(0, 0));

    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostBookings> _bookings = new();
    private readonly Mock<IHostEarnings> _earnings = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly Host _host = Host.CreateIndividual(UserId, AnyCpf, Guid.CreateVersion7(), AdminId, Now);

    public HostDashboardTests()
    {
        _bookings.Setup(b => b.GetOverviewAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyOverview);
        _earnings.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NoEarnings);
        _bookings.Setup(b => b.CountCompletedStaysAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private GetMyDashboardQueryHandler Handler() => new(_hosts.Object, _bookings.Object, _earnings.Object, _time);

    private void HostExists() =>
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_host);

    private static HostBookingSummary Booking(DateTimeOffset? responseDeadline = null, HostBookingOwnerRating? rating = null) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Cantinho da Cida",
        "Zona 7",
        Guid.CreateVersion7(),
        "Ana",
        null,
        rating,
        [new HostBookingPet(Guid.CreateVersion7(), "Pipoca", "dog", "small")],
        new DateOnly(2026, 10, 20),
        new DateOnly(2026, 10, 23),
        3,
        1,
        360m,
        Now,
        responseDeadline,
        null);

    [Fact]
    public async Task Handle_Should_ReturnHostNotFound_When_AccountWasNotApprovedYet()
    {
        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.ProfileNotFound);
        _bookings.VerifyNoOtherCalls();
        _earnings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_MapBookingsAndEarnings_When_HostExists()
    {
        // Arrange
        HostExists();
        var request = Booking(Now.AddHours(20), new HostBookingOwnerRating(4.8m, 5));
        var upcoming = Booking();
        var late = Booking();
        _bookings.Setup(b => b.GetOverviewAsync(_host.Id.Value, GetMyDashboardQueryHandler.ItemLimit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostBookingsOverview(2, [request], 1, 0, [], 1, [late], 7, [upcoming]));
        _earnings.Setup(e => e.GetAsync(_host.Id.Value, new DateOnly(2026, 10, 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostEarningsSummary(
                81_000,
                1_000,
                124_000,
                300_000,
                new HostEarningsBreakdown(95_000, 1_000, 5_000, 9_000),
                new HostHeldPayouts(22_400, 10_000, 0),
                new HostExpectedPayouts(27_000, 3)));
        // Outubro em Brasília: de 1º/10 00:00 (03:00 UTC) a 1º/11 00:00.
        _bookings.Setup(b => b.CountCompletedStaysAsync(
                _host.Id.Value,
                new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId), CancellationToken.None);

        // Assert
        var dashboard = result.Value!;
        dashboard.Earnings.Month.Should().Be("2026-10");
        dashboard.GeneratedAt.Should().Be(Now);
        dashboard.PendingRequests.Count.Should().Be(2);
        dashboard.PendingRequests.Items.Should().ContainSingle();
        var item = dashboard.PendingRequests.Items[0];
        item.BookingId.Should().Be(request.Id);
        item.Listing.Title.Should().Be("Cantinho da Cida");
        item.Owner.Name.Should().Be("Ana");
        item.OwnerRating!.Average.Should().Be(4.8m);
        item.ResponseDeadline.Should().Be(Now.AddHours(20));
        item.Pets.Should().ContainSingle(p => p.Name == "Pipoca");
        dashboard.AwaitingPayment.Count.Should().Be(1);
        dashboard.InProgress.Count.Should().Be(0);
        dashboard.OverdueCheckIns.Count.Should().Be(1);
        dashboard.OverdueCheckIns.Items.Should().ContainSingle(b => b.BookingId == late.Id);
        dashboard.UpcomingStays.Count.Should().Be(7);
        dashboard.UpcomingStays.Items.Should().ContainSingle(b => b.BookingId == upcoming.Id);
        dashboard.Earnings.Received.Should().Be(810m);
        dashboard.Earnings.Reversed.Should().Be(10m);
        dashboard.Earnings.Net.Should().Be(800m);
        dashboard.Earnings.ReceivedPreviousMonth.Should().Be(1240m);
        dashboard.Earnings.ReceivedYearToDate.Should().Be(3000m);
        dashboard.Earnings.CompletedStays.Should().Be(4);
        dashboard.Earnings.Breakdown.Should().Be(new HostDashboardEarningsBreakdownResponse(950m, 10m, 50m, 90m));
        dashboard.Earnings.Held.Should().Be(new HostDashboardHeldResponse(324m, 224m, 100m, 0m));
        dashboard.Earnings.Expected.Should().Be(new HostDashboardExpectedResponse(270m, 3));
        dashboard.Earnings.PayoutsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_UseBrasiliaMonth_When_UtcIsAlreadyNextMonth()
    {
        // Arrange: 1º de novembro, 01:00 UTC = 31 de outubro, 22:00 em Brasília.
        HostExists();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));

        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.Earnings.Month.Should().Be("2026-10");
        _earnings.Verify(e => e.GetAsync(_host.Id.Value, new DateOnly(2026, 10, 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_UseRequestedMonth_When_MonthIsInThePast()
    {
        // Arrange
        HostExists();

        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId, "2026-08"), CancellationToken.None);

        // Assert
        result.Value!.Earnings.Month.Should().Be("2026-08");
        _earnings.Verify(e => e.GetAsync(_host.Id.Value, new DateOnly(2026, 8, 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("10-2026")]
    [InlineData("2026-10-01")]
    [InlineData("outubro")]
    public async Task Handle_Should_ReturnMonthInvalid_When_FormatIsWrong(string month)
    {
        // Arrange
        HostExists();

        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId, month), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.DashboardMonthInvalid);
        _bookings.VerifyNoOtherCalls();
        _earnings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_ReturnMonthInFuture_When_MonthIsAfterTheCurrentOne()
    {
        // Arrange
        HostExists();

        // Act
        var result = await Handler().HandleAsync(new GetMyDashboardQuery(UserId, "2026-11"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.DashboardMonthInFuture);
        _earnings.VerifyNoOtherCalls();
    }
}
