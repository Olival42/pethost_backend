using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

/// <summary>Bloqueio de datas na agenda: datas, motivo, limite e sobreposição.</summary>
public sealed class AvailabilityBlockTests
{
    private static readonly DateTimeOffset Now = ListingBuilder.DefaultNow; // 11/10/2026, 09h em Brasília
    private static readonly DateOnly Today = new(2026, 10, 11);
    private static readonly ListingId Listing = ListingId.New();

    [Fact]
    public void Create_Should_BlockFromStartToEndInclusive_When_DatesAreValid()
    {
        // Act
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(9), Today.AddDays(15), "  Viagem  ", [], Now);

        // Assert
        var block = result.Value!;
        block.ListingId.Should().Be(Listing);
        block.StartDate.Should().Be(Today.AddDays(9));
        block.EndDate.Should().Be(Today.AddDays(15));
        block.Days.Should().Be(7);
        block.Reason.Should().Be("Viagem");
        block.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_AllowASingleDayStartingToday_When_ReasonIsBlank()
    {
        // Act
        var result = AvailabilityBlock.Create(Listing, Today, Today, "   ", [], Now);

        // Assert
        result.Value!.Days.Should().Be(1);
        result.Value.Reason.Should().BeNull();
    }

    [Fact]
    public void Create_Should_ReturnEveryFieldError_When_DatesAndReasonAreInvalid()
    {
        // Act — início no passado, fim antes do início, motivo longo demais.
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(-1), Today.AddDays(-2), new string('x', AvailabilityBlock.ReasonMaxLength + 1), [], Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([
            ListingsErrors.AvailabilityBlockStartInThePast,
            ListingsErrors.AvailabilityBlockEndBeforeStart,
            ListingsErrors.AvailabilityBlockReasonTooLong,
        ]);
    }

    [Fact]
    public void Create_Should_FailOnBothFields_When_TheBlockIsBeyondOneYear()
    {
        // Act
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(AvailabilityBlock.MaxDaysAhead + 1), Today.AddDays(AvailabilityBlock.MaxDaysAhead + 2), null, [], Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.AvailabilityBlockStartTooFarAhead, ListingsErrors.AvailabilityBlockEndTooFarAhead]);
    }

    [Fact]
    public void Create_Should_UseBrasiliaToday_When_ItIsStillYesterdayThere()
    {
        // Arrange — 02h UTC do dia 12 ainda é dia 11 em Brasília.
        var lateNight = new DateTimeOffset(2026, 10, 12, 2, 0, 0, TimeSpan.Zero);

        // Act
        var result = AvailabilityBlock.Create(Listing, Today, Today, null, [], lateNight);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(5, 8)]   // começa dentro
    [InlineData(1, 3)]   // termina no primeiro dia
    [InlineData(10, 12)] // começa no último dia
    [InlineData(1, 20)]  // engloba
    public void Create_Should_FailWithOverlap_When_ItTouchesAnotherBlock(int fromDays, int toDays)
    {
        // Arrange — já fechado de hoje+3 a hoje+10.
        var existing = AvailabilityBlock.Create(Listing, Today.AddDays(3), Today.AddDays(10), null, [], Now).Value!;

        // Act
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(fromDays), Today.AddDays(toDays), null, [existing], Now);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.AvailabilityBlockOverlap(Today.AddDays(3), Today.AddDays(10)));
    }

    [Fact]
    public void Create_Should_Succeed_When_ItStartsTheDayAfterAnotherBlock()
    {
        // Arrange
        var existing = AvailabilityBlock.Create(Listing, Today.AddDays(3), Today.AddDays(10), null, [], Now).Value!;

        // Act
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(11), Today.AddDays(12), null, [existing], Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_FailWithLimitReached_When_TheListingHasTheMaximumOfOpenBlocks()
    {
        // Arrange
        var open = Enumerable.Range(0, AvailabilityBlock.MaxOpenBlocks)
            .Select(i => AvailabilityBlock.Create(Listing, Today.AddDays(i * 2), Today.AddDays(i * 2), null, [], Now).Value!)
            .ToList();

        // Act
        var result = AvailabilityBlock.Create(Listing, Today.AddDays(200), Today.AddDays(201), null, open, Now);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.AvailabilityBlockLimitReached);
    }

    [Fact]
    public void HasEnded_Should_BeTrueOnlyAfterTheLastDay_When_Checked()
    {
        // Arrange
        var block = AvailabilityBlock.Create(Listing, Today, Today.AddDays(2), null, [], Now).Value!;

        // Assert
        block.HasEnded(Today.AddDays(2)).Should().BeFalse();
        block.HasEnded(Today.AddDays(3)).Should().BeTrue();
        block.Overlaps(Today.AddDays(2), Today.AddDays(5)).Should().BeTrue();
        block.Overlaps(Today.AddDays(3), Today.AddDays(5)).Should().BeFalse();
    }
}
