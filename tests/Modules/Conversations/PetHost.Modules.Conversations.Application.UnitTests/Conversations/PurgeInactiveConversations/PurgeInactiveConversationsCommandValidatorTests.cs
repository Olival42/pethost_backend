using FluentAssertions;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Modules.Conversations.Domain.Errors;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.PurgeInactiveConversations;

public sealed class PurgeInactiveConversationsCommandValidatorTests
{
    private readonly PurgeInactiveConversationsCommandValidator _sut = new();

    [Theory]
    [InlineData(90, 100)]
    [InlineData(1, 1)]
    [InlineData(30, 1000)]
    public void Validate_Should_Pass_When_TheConfigurationIsInRange(int inactivityDays, int batchSize)
    {
        // Act
        var result = _sut.Validate(new PurgeInactiveConversationsCommand(inactivityDays, batchSize));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-90)]
    public void Validate_Should_ReturnInactivityDaysInvalid_When_TheDaysAreNotPositive(int inactivityDays)
    {
        // Act
        var result = _sut.Validate(new PurgeInactiveConversationsCommand(inactivityDays, BatchSize: 100));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.InactivityDaysInvalid.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Validate_Should_ReturnBatchSizeInvalid_When_TheBatchIsOutOfRange(int batchSize)
    {
        // Act
        var result = _sut.Validate(new PurgeInactiveConversationsCommand(InactivityDays: 90, batchSize));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.BatchSizeInvalid.Message);
    }
}
