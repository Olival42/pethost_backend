using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Conversations;

public sealed class ConversationIdTests
{
    [Fact]
    public void New_Should_GenerateAVersion7Id_When_Called()
    {
        // Act
        var id = ConversationId.New();

        // Assert
        id.Value.Version.Should().Be(7);
    }

    [Fact]
    public void ToString_Should_ReturnTheGuid_When_Formatted()
    {
        // Arrange
        var id = ConversationId.New();

        // Act
        var text = id.ToString();

        // Assert
        text.Should().Be(id.Value.ToString());
    }
}
