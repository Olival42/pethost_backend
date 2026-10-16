using FluentAssertions;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.SendPhotoMessage;

public sealed class SendPhotoMessageCommandValidatorTests
{
    private readonly SendPhotoMessageCommandValidator _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Brincando no quintal")]
    public void Validate_Should_Pass_When_TheCaptionIsOptionalOrShort(string? caption)
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), caption));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnConversationIdRequired_When_TheConversationIsEmpty()
    {
        // Act
        var result = _sut.Validate(Command(Guid.Empty, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ConversationId" && e.ErrorMessage == ConversationsErrors.ConversationIdRequired.Message);
    }

    [Fact]
    public void Validate_Should_ReturnBodyTooLong_When_TheCaptionIsLongerThanTheLimit()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), new string('a', Message.BodyMaxLength + 1)));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Body" && e.ErrorMessage == ConversationsErrors.BodyTooLong.Message);
    }

    [Fact]
    public void Validate_Should_ReturnBodyInvalidCharacters_When_TheCaptionHasAControlCharacter()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), "legenda\0"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Body" && e.ErrorMessage == ConversationsErrors.BodyInvalidCharacters.Message);
    }

    private static SendPhotoMessageCommand Command(Guid conversationId, string? caption) =>
        new(ConversationBuilder.HostUserId, Roles.Host, conversationId, Image: null, caption);
}
