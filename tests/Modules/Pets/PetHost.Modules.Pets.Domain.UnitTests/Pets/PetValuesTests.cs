using FluentAssertions;
using PetHost.Modules.Pets.Domain.Pets;
using Xunit;

namespace PetHost.Modules.Pets.Domain.UnitTests.Pets;

public sealed class PetValuesTests
{
    [Fact]
    public void PetSpeciesValues_Should_CoverEveryEnumValue_When_EnumGrows()
    {
        // Act / Assert
        Enum.GetValues<PetSpecies>().Select(PetSpeciesValues.ToWire).Should().BeEquivalentTo(PetSpeciesValues.All);
    }

    [Fact]
    public void PetSizeValues_Should_CoverEveryEnumValue_When_EnumGrows()
    {
        // Act / Assert
        Enum.GetValues<PetSize>().Select(PetSizeValues.ToWire).Should().BeEquivalentTo(PetSizeValues.All);
    }

    [Fact]
    public void PetSexValues_Should_CoverEveryEnumValue_When_EnumGrows()
    {
        // Act / Assert
        Enum.GetValues<PetSex>().Select(PetSexValues.ToWire).Should().BeEquivalentTo(PetSexValues.All);
    }

    [Theory]
    [InlineData("guinea_pig", true)]
    [InlineData("GuineaPig", false)]
    [InlineData(null, false)]
    public void PetSpeciesValues_Should_ParseOnlyWireValues_When_Parsing(string? value, bool expected)
    {
        // Act / Assert
        PetSpeciesValues.TryParse(value, out _).Should().Be(expected);
    }

    [Fact]
    public void PetKeeper_Should_Throw_When_IdIsEmpty()
    {
        // Act
        var act = () => PetKeeper.Host(Guid.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PetKeeper_Should_NameItsType_When_Asked()
    {
        // Act / Assert
        PetKeeper.Owner(Guid.CreateVersion7()).TypeName.Should().Be("owner");
        PetKeeper.Host(Guid.CreateVersion7()).TypeName.Should().Be("host");
    }
}
