using FluentAssertions;
using PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;
using PetHost.Modules.Conversations.Domain.Errors;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.NotifyUnreadMessages;

public sealed class NotifyUnreadMessagesCommandValidatorTests
{
    private readonly NotifyUnreadMessagesCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_TheDefaultsAreUsed()
    {
        // Act
        var result = _sut.Validate(new NotifyUnreadMessagesCommand(15, 60, 200));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnEveryError_When_AllValuesAreOutOfRange()
    {
        // Act
        var result = _sut.Validate(new NotifyUnreadMessagesCommand(0, 0, ConversationsErrors.MaxRetentionBatchSize + 1));

        // Assert
        result.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(
            [ConversationsErrors.UnreadDelayInvalid.Message, ConversationsErrors.UnreadCooldownInvalid.Message, ConversationsErrors.BatchSizeInvalid.Message]);
    }
}
