using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Conversations;

public sealed class ConversationPartyValuesTests
{
    [Theory]
    [InlineData(ConversationParty.Owner, "owner")]
    [InlineData(ConversationParty.Host, "host")]
    public void ToWire_Should_ReturnTheValueOfTheSide_When_Converted(ConversationParty party, string expected)
    {
        // Act
        var wire = ConversationPartyValues.ToWire(party);

        // Assert
        wire.Should().Be(expected);
    }

    [Theory]
    [InlineData("owner", ConversationParty.Owner)]
    [InlineData("host", ConversationParty.Host)]
    public void FromWire_Should_ReturnTheSide_When_ValueIsKnown(string value, ConversationParty expected)
    {
        // Act
        var party = ConversationPartyValues.FromWire(value);

        // Assert
        party.Should().Be(expected);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Owner")]
    [InlineData("")]
    public void FromWire_Should_Throw_When_ValueIsUnknown(string value)
    {
        // Act
        var read = () => ConversationPartyValues.FromWire(value);

        // Assert
        read.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ConversationParty.Owner, ConversationParty.Host)]
    [InlineData(ConversationParty.Host, ConversationParty.Owner)]
    public void Other_Should_ReturnTheOppositeSide_When_GivenASide(ConversationParty party, ConversationParty expected)
    {
        // Act
        var other = ConversationPartyValues.Other(party);

        // Assert
        other.Should().Be(expected);
    }

    [Fact]
    public void All_Should_ListBothSides_When_Read()
    {
        // Act
        var all = ConversationPartyValues.All;

        // Assert
        all.Should().Equal("owner", "host");
    }
}
