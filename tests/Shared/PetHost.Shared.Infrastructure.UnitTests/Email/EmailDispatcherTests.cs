using FluentAssertions;
using PetHost.Shared.Infrastructure.Email;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Email;

public sealed class EmailDispatcherTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(7, 1920)]
    public void RetryDelay_Should_DoubleAfterEachFailure(int attempts, int expectedSeconds)
    {
        // Act
        var delay = EmailDispatcher.RetryDelay(attempts);

        // Assert
        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(50)]
    public void RetryDelay_Should_NeverExceedOneHour(int attempts)
    {
        // Act
        var delay = EmailDispatcher.RetryDelay(attempts);

        // Assert
        delay.Should().Be(TimeSpan.FromHours(1));
    }
}
