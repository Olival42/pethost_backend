using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Reviews;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reviews;

public sealed class ReviewIdTests
{
    [Fact]
    public void New_Should_CreateDistinctIds_When_CalledTwice()
    {
        // Act
        var first = ReviewId.New();
        var second = ReviewId.New();

        // Assert
        first.Should().NotBe(second);
        first.Value.Version.Should().Be(7);
    }

    [Fact]
    public void ToString_Should_ReturnTheGuid_When_Called()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act & Assert
        new ReviewId(value).ToString().Should().Be(value.ToString());
    }
}
