using FluentAssertions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Domain.UnitTests.Pets;

public sealed class PetProfileTests
{
    private static readonly DateTimeOffset Now = PetProfileBuilder.DefaultNow;

    private static PetProfileData Valid => new PetProfileBuilder().BuildData();

    [Fact]
    public void Create_Should_ParseAndKeepEveryField_When_DataIsValid()
    {
        // Act
        var result = PetProfile.Create(Valid, Now);

        // Assert
        var profile = result.Value!;
        profile.Species.Should().Be(PetSpecies.Dog);
        profile.Name.Should().Be("Pipoca");
        profile.Size.Should().Be(PetSize.Medium);
        profile.BirthDate.Should().Be(new DateOnly(2022, 3, 15));
        profile.Sex.Should().Be(PetSex.Female);
        profile.IsNeutered.Should().BeTrue();
        profile.GoodWithCats.Should().BeFalse();
        profile.Notes.Should().Be("Morre de medo de fogos.");
        profile.SpeciesDescription.Should().BeNull();
    }

    [Fact]
    public void Create_Should_ReturnEveryRequiredError_When_EverythingIsMissing()
    {
        // Arrange
        var empty = new PetProfileData(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        // Act
        var result = PetProfile.Create(empty, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            ["species", "name", "sex", "isNeutered", "isVaccinated", "goodWithDogs", "goodWithCats", "goodWithKids"]);
    }

    [Theory]
    [InlineData("Dog", PetSpecies.Dog)]
    [InlineData(" guinea_pig ", PetSpecies.GuineaPig)]
    [InlineData("COCKATIEL", PetSpecies.Cockatiel)]
    public void Create_Should_AcceptSpecies_When_CaseOrSpacesDiffer(string species, PetSpecies expected)
    {
        // Arrange
        var data = Valid with { Species = species, Size = species.Trim().Equals("dog", StringComparison.OrdinalIgnoreCase) ? "small" : null };

        // Act
        var result = PetProfile.Create(data, Now);

        // Assert
        result.Value!.Species.Should().Be(expected);
    }

    [Fact]
    public void Create_Should_RejectSpecies_When_ItIsNotInTheList()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "dragon" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "species" && e.Message.Contains("guinea_pig", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_Should_RequireSize_When_PetIsADog()
    {
        // Act
        var result = PetProfile.Create(Valid with { Size = null }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "size" && e.Message == "Size is required for dogs.");
    }

    [Fact]
    public void Create_Should_RejectSize_When_PetIsNeitherDogNorCat()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "rabbit", Size = "small" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "size" && e.Message == "Size only applies to dogs and cats.");
    }

    [Theory]
    [InlineData("large", PetSize.Large)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Create_Should_AcceptOptionalSize_When_PetIsACat(string? size, PetSize? expected)
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "cat", Size = size }, Now);

        // Assert
        result.Value!.Size.Should().Be(expected);
    }

    [Fact]
    public void Create_Should_RejectUnknownSize_When_PetIsACat()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "cat", Size = "giant" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "size");
    }

    [Theory]
    [InlineData(PetSpecies.Dog, true)]
    [InlineData(PetSpecies.Cat, true)]
    [InlineData(PetSpecies.Rabbit, false)]
    [InlineData(PetSpecies.Exotic, false)]
    public void HasSize_Should_BeTrueOnlyForDogsAndCats_When_Asked(PetSpecies species, bool expected)
    {
        // Act / Assert
        PetProfile.HasSize(species).Should().Be(expected);
    }

    [Fact]
    public void Create_Should_RejectSize_When_ValueIsUnknown()
    {
        // Act
        var result = PetProfile.Create(Valid with { Size = "giant" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "size");
    }

    [Fact]
    public void Create_Should_RequireDescription_When_PetIsExotic()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "exotic", Size = null, SpeciesDescription = " " }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "speciesDescription" && e.Message.Contains("exotic", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_Should_KeepDescription_When_PetIsExotic()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "exotic", Size = null, SpeciesDescription = " Iguana verde " }, Now);

        // Assert
        result.Value!.SpeciesDescription.Should().Be("Iguana verde");
    }

    [Fact]
    public void Create_Should_RejectDescription_When_PetIsNotExotic()
    {
        // Act
        var result = PetProfile.Create(Valid with { SpeciesDescription = "Vira-lata" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "speciesDescription" && e.Message == "Species description is only for exotic animals.");
    }

    [Fact]
    public void Create_Should_RejectDescription_When_ItIsTooLong()
    {
        // Act
        var result = PetProfile.Create(
            Valid with { Species = "exotic", Size = null, SpeciesDescription = new string('x', PetProfile.SpeciesDescriptionMaxLength + 1) },
            Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "speciesDescription");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/fotos/pipoca.png")]
    [InlineData("ftp://cdn.pethost.com/pipoca.png")]
    public void Create_Should_RejectPhotoUrl_When_ItIsNotAWebAddress(string url)
    {
        // Act
        var result = PetProfile.Create(Valid with { PhotoUrl = url }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "photoUrl");
    }

    [Fact]
    public void Create_Should_RejectPhotoUrl_When_ItIsTooLong()
    {
        // Act
        var result = PetProfile.Create(Valid with { PhotoUrl = "https://cdn.pethost.com/" + new string('a', 500) }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "photoUrl" && e.Message.Contains("500", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("15/03/2022", "Birth date must be a valid date in the format yyyy-MM-dd.")]
    [InlineData("2026-10-10", "Birth date cannot be in the future.")]
    public void Create_Should_RejectBirthDate_When_ItIsInvalid(string birthDate, string message)
    {
        // Act
        var result = PetProfile.Create(Valid with { BirthDate = birthDate }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "birthDate" && e.Message == message);
    }

    [Fact]
    public void Create_Should_AcceptToday_When_PetWasBornToday()
    {
        // Act
        var result = PetProfile.Create(Valid with { BirthDate = "2026-10-09" }, Now);

        // Assert
        result.Value!.BirthDate.Should().Be(new DateOnly(2026, 10, 9));
    }

    [Fact]
    public void Create_Should_RejectSex_When_ValueIsUnknownToTheList()
    {
        // Act
        var result = PetProfile.Create(Valid with { Sex = "m" }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "sex");
    }

    [Fact]
    public void Create_Should_AcceptUnknownSex_When_ItCannotBeTold()
    {
        // Act
        var result = PetProfile.Create(Valid with { Species = "parrot", Size = null, Sex = "unknown" }, Now);

        // Assert
        result.Value!.Sex.Should().Be(PetSex.Unknown);
    }

    [Fact]
    public void Create_Should_TurnEmptyOptionalTextIntoNull_When_EmptyTextIsSent()
    {
        // Act
        var result = PetProfile.Create(
            Valid with { PhotoUrl = "", Breed = " ", BirthDate = "", FeedingNotes = "", VetContact = "", Notes = "", MedicationNotes = "" },
            Now);

        // Assert
        var profile = result.Value!;
        profile.PhotoUrl.Should().BeNull();
        profile.Breed.Should().BeNull();
        profile.BirthDate.Should().BeNull();
        profile.FeedingNotes.Should().BeNull();
        profile.VetContact.Should().BeNull();
        profile.Notes.Should().BeNull();
        profile.MedicationNotes.Should().BeNull();
    }

    [Fact]
    public void Create_Should_RejectEveryTooLongText_When_LimitsArePassed()
    {
        // Arrange
        var data = Valid with
        {
            Name = new string('n', PetProfile.NameMaxLength + 1),
            Breed = new string('b', PetProfile.BreedMaxLength + 1),
            MedicationNotes = new string('m', PetProfile.CareNotesMaxLength + 1),
            FeedingNotes = new string('f', PetProfile.CareNotesMaxLength + 1),
            VetContact = new string('v', PetProfile.VetContactMaxLength + 1),
            Notes = new string('o', PetProfile.NotesMaxLength + 1),
        };

        // Act
        var result = PetProfile.Create(data, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            ["name", "breed", "medicationNotes", "feedingNotes", "vetContact", "notes"]);
    }

    [Fact]
    public void Create_Should_KeepHealthDetails_When_Sent()
    {
        // Act
        var result = PetProfile.Create(Valid with { WeightKg = 0.0354m, Microchip = " 985 112-004 567 890 ", Allergies = " Frango " }, Now);

        // Assert
        var profile = result.Value!;
        profile.WeightKg.Should().Be(0.035m, "peso guardado em gramas: 3 casas");
        profile.Microchip.Should().Be("985112004567890", "só os dígitos");
        profile.Allergies.Should().Be("Frango");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(150.001)]
    public void Create_Should_RejectWeight_When_OutOfRange(double weight)
    {
        // Act
        var result = PetProfile.Create(Valid with { WeightKg = (decimal)weight }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "weightKg");
    }

    [Theory]
    [InlineData("98511200456789")]
    [InlineData("9851120045678901")]
    [InlineData("98511200456789A")]
    public void Create_Should_RejectMicrochip_When_ItIsNotFifteenDigits(string microchip)
    {
        // Act
        var result = PetProfile.Create(Valid with { Microchip = microchip }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "microchip" && e.Message == "Microchip number must have 15 digits.");
    }

    [Fact]
    public void Create_Should_RejectAllergies_When_TooLong()
    {
        // Act
        var result = PetProfile.Create(Valid with { Allergies = new string('a', PetProfile.CareNotesMaxLength + 1) }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "allergies");
    }

    [Fact]
    public void Create_Should_ClearMicrochipAndAllergies_When_EmptyTextIsSent()
    {
        // Act
        var result = PetProfile.Create(Valid with { Microchip = "", Allergies = "" }, Now);

        // Assert
        result.Value!.Microchip.Should().BeNull();
        result.Value.Allergies.Should().BeNull();
    }

    [Fact]
    public void Create_Should_RequireName_When_ItIsBlank()
    {
        // Act
        var result = PetProfile.Create(Valid with { Name = "  " }, Now);

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "name" && e.Message == "Name is required.");
    }

    [Fact]
    public void ToData_Should_RoundTrip_When_FedBackIntoCreate()
    {
        // Arrange
        var profile = PetProfile.Create(Valid, Now).Value!;

        // Act
        var again = PetProfile.Create(profile.ToData(), Now).Value!;

        // Assert
        again.Should().Be(profile);
    }

    [Fact]
    public void Equals_Should_BeFalse_When_AnyFieldDiffers()
    {
        // Arrange
        var profile = PetProfile.Create(Valid, Now).Value!;

        // Act
        var other = PetProfile.Create(Valid with { GoodWithCats = true }, Now).Value!;

        // Assert
        other.Should().NotBe(profile);
    }
}
