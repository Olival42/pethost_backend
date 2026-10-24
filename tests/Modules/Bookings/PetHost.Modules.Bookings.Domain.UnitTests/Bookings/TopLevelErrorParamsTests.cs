using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>O front lê data e id do pet de <c>error.params</c>, nunca da frase em inglês.</summary>
public sealed class TopLevelErrorParamsTests
{
    private static readonly Guid PetId = Guid.Parse("0199a000-0000-7000-8000-000000000004");

    private static Dictionary<string, object> Params(Error error) => error.Params!.Values.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void WeekdayNotAvailable_Should_CarryTheClosedDay()
    {
        var error = BookingsErrors.WeekdayNotAvailable(new DateOnly(2026, 11, 8));

        error.Code.Should().Be("BOOKING_WEEKDAY_NOT_AVAILABLE");
        Params(error).Should().BeEquivalentTo(new Dictionary<string, object> { ["date"] = "2026-11-08" });
    }

    [Fact]
    public void CapacityExceeded_Should_CarryTheFullNight()
    {
        var error = BookingsErrors.CapacityExceeded(new DateOnly(2026, 12, 25));

        Params(error).Should().BeEquivalentTo(new Dictionary<string, object> { ["date"] = "2026-12-25" });
    }

    [Fact]
    public void DatesBlocked_And_CheckInTooEarly_Should_CarryTheDate()
    {
        Params(BookingsErrors.DatesBlocked(new DateOnly(2026, 11, 9))).Should().BeEquivalentTo(new Dictionary<string, object> { ["date"] = "2026-11-09" });
        Params(BookingsErrors.CheckInTooEarly(new DateOnly(2026, 11, 10))).Should().BeEquivalentTo(new Dictionary<string, object> { ["date"] = "2026-11-10" });
    }

    [Fact]
    public void PetPeriodConflict_Should_CarryThePetIdAndNeverTheName()
    {
        var error = BookingsErrors.PetPeriodConflict(PetId, "Pipoca");

        error.Message.Should().Contain("Pipoca");
        Params(error).Should().BeEquivalentTo(new Dictionary<string, object> { ["petId"] = PetId.ToString() });
        Params(error).Values.Should().NotContain("Pipoca");
    }

    [Fact]
    public void PetRules_Should_CarryThePetIdAndTheRejectedValue()
    {
        Params(BookingsErrors.PetInactive(PetId, "Pipoca")).Should().BeEquivalentTo(new Dictionary<string, object> { ["petId"] = PetId.ToString() });
        Params(BookingsErrors.SpeciesNotAccepted(PetId, "Pipoca", "dog")).Should().BeEquivalentTo(new Dictionary<string, object> { ["petId"] = PetId.ToString(), ["species"] = "dog" });
        Params(BookingsErrors.SizeNotAccepted(PetId, "Pipoca", "large")).Should().BeEquivalentTo(new Dictionary<string, object> { ["petId"] = PetId.ToString(), ["size"] = "large" });
    }
}
