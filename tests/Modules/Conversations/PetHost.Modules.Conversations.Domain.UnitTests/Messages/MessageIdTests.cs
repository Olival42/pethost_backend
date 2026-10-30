using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Messages;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Messages;

public sealed class MessageIdTests
{
    [Fact]
    public void New_Should_GenerateAVersion7Id_When_Called()
    {
        // Act
        var id = MessageId.New();

        // Assert
        id.Value.Version.Should().Be(7);
    }

    [Fact]
    public void ToString_Should_ReturnTheGuid_When_Formatted()
    {
        // Arrange
        var id = MessageId.New();

        // Act
        var text = id.ToString();

        // Assert
        text.Should().Be(id.Value.ToString());
    }
}
