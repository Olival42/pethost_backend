using FluentAssertions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

/// <summary>
/// "Combina com seu pet". O cantinho padrão do <see cref="ListingBuilder"/> (casa da Dona Cida):
/// aceita cachorro e gato, pequeno e médio; na casa tem cão, sem gato e sem criança.
/// </summary>
public sealed class PetCompatibilityTests
{
    private readonly Listing _listing = new ListingBuilder().Build();

    [Fact]
    public void Check_Should_ReturnNoWarning_When_PetGetsAlongWithEverythingAndThereAreNoGuests()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca(), []);

        // Assert
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Check_Should_WarnSpeciesNotAccepted_When_ListingDoesNotAcceptTheSpecies()
    {
        // Arrange
        var rabbit = Pipoca() with { Species = "rabbit", Size = null };

        // Act
        var warnings = PetCompatibility.Check(_listing, rabbit, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.SpeciesNotAccepted);
    }

    [Fact]
    public void Check_Should_WarnSpeciesNotAccepted_When_SpeciesIsUnknown()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { Species = "dragon" }, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.SpeciesNotAccepted);
    }

    [Fact]
    public void Check_Should_WarnSizeNotAccepted_When_DogIsLargeAndListingDoesNotAcceptLarge()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { Size = "large" }, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.SizeNotAccepted);
    }

    [Theory]
    [InlineData("small", false, true)]
    [InlineData("medium", true, false)]
    public void Check_Should_WarnSizeNotAccepted_When_ListingRefusesThatSize(string size, bool acceptsSmall, bool acceptsMedium)
    {
        // Arrange
        var listing = new ListingBuilder()
            .WithDetails(d => d with { AcceptsSmall = acceptsSmall, AcceptsMedium = acceptsMedium, AcceptsLarge = true })
            .Build();

        // Act
        var warnings = PetCompatibility.Check(listing, Pipoca() with { Size = size }, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.SizeNotAccepted);
    }

    [Fact]
    public void Check_Should_NotWarnAboutSize_When_DogHasNoSize()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { Size = null }, []);

        // Assert
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Check_Should_WarnHomeHasDogs_When_PetDoesNotGetAlongWithDogsAndTheHomeHasDogs()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { GoodWithDogs = false }, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.HomeHasDogs);
    }

    [Fact]
    public void Check_Should_WarnAboutEveryoneInTheHome_When_PetGetsAlongWithNobody()
    {
        // Arrange
        var listing = new ListingBuilder().WithDetails(d => d with { HasDogs = true, HasCats = true, HasKids = true }).Build();
        var pet = Pipoca() with { GoodWithDogs = false, GoodWithCats = false, GoodWithKids = false };

        // Act
        var warnings = PetCompatibility.Check(listing, pet, []);

        // Assert
        Codes(warnings).Should().Equal(PetCompatibility.HomeHasDogs, PetCompatibility.HomeHasCats, PetCompatibility.HomeHasKids);
        warnings.Should().OnlyContain(w => w.Guest == null);
    }

    [Fact]
    public void Check_Should_NotWarn_When_PetDoesNotGetAlongWithCatsButTheHomeHasNoCats()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { GoodWithCats = false, GoodWithKids = false }, []);

        // Assert
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Check_Should_WarnGuestIsCat_When_PetDoesNotGetAlongWithCatsAndACatStaysInThePeriod()
    {
        // Arrange
        var mimi = Guest("Mimi", "cat", "small");

        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { GoodWithCats = false }, [mimi]);

        // Assert
        warnings.Should().Equal(new CompatibilityWarning(PetCompatibility.GuestIsCat, mimi));
    }

    [Fact]
    public void Check_Should_WarnGuestIsDog_When_PetDoesNotGetAlongWithDogsAndADogStaysInThePeriod()
    {
        // Arrange
        var listing = new ListingBuilder().WithDetails(d => d with { HasDogs = false }).Build();
        var rex = Guest("Rex", "dog", "medium");

        // Act
        var warnings = PetCompatibility.Check(listing, Pipoca() with { GoodWithDogs = false }, [rex]);

        // Assert
        warnings.Should().Equal(new CompatibilityWarning(PetCompatibility.GuestIsDog, rex));
    }

    [Fact]
    public void Check_Should_WarnGuestDislikesDogs_When_PetIsADogAndAGuestDoesNotGetAlongWithDogs()
    {
        // Arrange
        var mimi = Guest("Mimi", "cat", "small") with { GoodWithDogs = false };

        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca(), [mimi]);

        // Assert
        warnings.Should().Equal(new CompatibilityWarning(PetCompatibility.GuestDislikesDogs, mimi));
    }

    [Fact]
    public void Check_Should_WarnGuestDislikesCats_When_PetIsACatAndAGuestDoesNotGetAlongWithCats()
    {
        // Arrange
        var listing = new ListingBuilder().WithDetails(d => d with { HasDogs = false }).Build();
        var cat = Pipoca() with { Name = "Frajola", Species = "cat", Size = "small" };
        var rex = Guest("Rex", "dog", "medium") with { GoodWithCats = false };

        // Act
        var warnings = PetCompatibility.Check(listing, cat, [rex]);

        // Assert
        warnings.Should().Equal(new CompatibilityWarning(PetCompatibility.GuestDislikesCats, rex));
    }

    [Fact]
    public void Check_Should_WarnGuestLargeDog_When_PetIsSmallAndALargeDogStaysInThePeriod()
    {
        // Arrange
        var thor = Guest("Thor", "dog", "large");

        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca() with { Size = "small" }, [thor]);

        // Assert
        warnings.Should().Equal(new CompatibilityWarning(PetCompatibility.GuestLargeDog, thor));
    }

    [Fact]
    public void Check_Should_NotWarnAboutLargeDogs_When_PetIsNotSmall()
    {
        // Act
        var warnings = PetCompatibility.Check(_listing, Pipoca(), [Guest("Thor", "dog", "large")]);

        // Assert
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Check_Should_ListHomeWarningsFirstAndThenEachGuestInOrder_When_ManyRulesApply()
    {
        // Arrange
        var mimi = Guest("Mimi", "cat", "small");
        var thor = Guest("Thor", "dog", "large") with { GoodWithDogs = false };
        var pet = Pipoca() with { Size = "small", GoodWithDogs = false, GoodWithCats = false };

        // Act
        var warnings = PetCompatibility.Check(_listing, pet, [mimi, thor]);

        // Assert
        warnings.Should().Equal(
            new CompatibilityWarning(PetCompatibility.HomeHasDogs),
            new CompatibilityWarning(PetCompatibility.GuestIsCat, mimi),
            new CompatibilityWarning(PetCompatibility.GuestIsDog, thor),
            new CompatibilityWarning(PetCompatibility.GuestDislikesDogs, thor),
            new CompatibilityWarning(PetCompatibility.GuestLargeDog, thor));
    }

    [Fact]
    public void All_Should_ListEveryCodeOnce()
    {
        // Assert
        PetCompatibility.All.Should().HaveCount(10).And.OnlyHaveUniqueItems();
    }

    /// <summary>A Pipoca: cachorro médio que se dá com todo mundo.</summary>
    private static CompatibilityPet Pipoca() =>
        new(Guid.CreateVersion7(), "Pipoca", "dog", "medium", GoodWithDogs: true, GoodWithCats: true, GoodWithKids: true);

    private static CompatibilityPet Guest(string name, string species, string? size) =>
        new(Guid.CreateVersion7(), name, species, size, GoodWithDogs: true, GoodWithCats: true, GoodWithKids: true);

    private static List<string> Codes(IReadOnlyList<CompatibilityWarning> warnings) => [.. warnings.Select(w => w.Code)];
}
