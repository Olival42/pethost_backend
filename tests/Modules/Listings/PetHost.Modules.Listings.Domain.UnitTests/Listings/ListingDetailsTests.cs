using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class ListingDetailsTests
{
    private readonly ListingBuilder _builder = new();

    [Fact]
    public void Create_Should_NormalizeEverything_When_DataIsValid()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with
        {
            Title = "  Casa com quintal  ",
            HomeType = "HOUSE",
            AcceptedSpecies = ["Dog", " cat ", "dog"],
            AvailableWeekdays = ["Saturday", "sunday"],
            HouseRules = "   ",
        }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Casa com quintal");
        result.Value.HomeType.Should().Be(HomeType.House);
        result.Value.AcceptedSpecies.Should().Be(AcceptedSpecies.Dog | AcceptedSpecies.Cat);
        result.Value.AvailableWeekdays.Should().Be(Weekdays.Saturday | Weekdays.Sunday);
        result.Value.HouseRules.Should().BeNull("texto em branco num opcional vira nulo");
    }

    [Fact]
    public void Create_Should_ReturnEveryRequiredError_When_DataIsEmpty()
    {
        // Arrange
        var data = new ListingDetailsData(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
        [
            "title", "description", "homeType", "hasYard", "hasDogs", "hasCats", "hasKids", "acceptedSpecies",
            "acceptsSmall", "acceptsMedium", "acceptsLarge", "capacity", "nightlyRate", "availableWeekdays",
        ]);
    }

    [Fact]
    public void Create_Should_AllowMissingHomeType_When_HostIsCompany()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { HomeType = null }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.HomeType.Should().BeNull();
    }

    [Fact]
    public void Create_Should_RequireHomeType_When_HostIsIndividual()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { HomeType = "" }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.HomeTypeRequired);
    }

    [Fact]
    public void Create_Should_RejectHomeType_When_ValueIsUnknown()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { HomeType = "castle" }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: true);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.HomeTypeInvalid);
    }

    [Theory]
    [InlineData(10, false, true)]
    [InlineData(11, false, false)]
    [InlineData(11, true, true)]
    [InlineData(200, true, true)]
    [InlineData(201, true, false)]
    [InlineData(0, true, false)]
    public void Create_Should_ApplyTheCapacityCeilingOfTheHost(int capacity, bool isCompany, bool valid)
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { Capacity = capacity }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany);

        // Assert
        result.IsSuccess.Should().Be(valid);
    }

    [Fact]
    public void Create_Should_SayTheCeilingOfTheHost_When_CapacityIsTooHigh()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { Capacity = 50 }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("10");
    }

    [Theory]
    [InlineData("9.99", false)]
    [InlineData("10", true)]
    [InlineData("60.5", true)]
    [InlineData("10000.00", true)]
    [InlineData("10000.01", false)]
    public void Create_Should_KeepTheNightlyRateInRange(string rate, bool valid)
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { NightlyRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.IsSuccess.Should().Be(valid);
    }

    [Fact]
    public void Create_Should_RejectFractionOfCents_When_NightlyRateHasThreeDecimals()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { NightlyRate = 60.555m }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.NightlyRateTooPrecise);
    }

    [Fact]
    public void Create_Should_RequireASize_When_DogsAreAccepted()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { AcceptsSmall = false, AcceptsMedium = false, AcceptsLarge = false }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.SizeRequiredForDogsAndCats);
    }

    [Fact]
    public void Create_Should_NotRequireASize_When_OnlyBirdsAreAccepted()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with
        {
            AcceptedSpecies = ["cockatiel", "parrot"],
            AcceptsSmall = false,
            AcceptsMedium = false,
            AcceptsLarge = false,
        }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_RejectUnknownSpeciesAndWeekdays()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with { AcceptedSpecies = ["dog", "dragon"], AvailableWeekdays = ["funday"] }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.AcceptedSpeciesInvalid, ListingsErrors.AvailableWeekdaysInvalid]);
    }

    [Fact]
    public void Create_Should_RejectTextsAboveTheLimit()
    {
        // Arrange
        var data = _builder.WithDetails(d => d with
        {
            Title = new string('a', ListingDetails.TitleMaxLength + 1),
            Description = new string('a', ListingDetails.DescriptionMaxLength + 1),
            HouseRules = new string('a', ListingDetails.HouseRulesMaxLength + 1),
        }).BuildDetailsData();

        // Act
        var result = ListingDetails.Create(data, isCompany: false);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.TitleTooLong, ListingsErrors.DescriptionTooLong, ListingsErrors.HouseRulesTooLong]);
    }

    [Fact]
    public void ToData_Should_RoundTrip_When_RecreatedFromItsOwnData()
    {
        // Arrange
        var details = _builder.BuildDetails();

        // Act
        var again = ListingDetails.Create(details.ToData(), isCompany: false);

        // Assert
        again.Value.Should().Be(details);
    }
}
