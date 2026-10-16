using FluentAssertions;
using PetHost.Modules.Conversations.Application.Messages.SendMessage;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.SendMessage;

public sealed class SendMessageCommandValidatorTests
{
    private readonly SendMessageCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_ConversationAndTextAreValid()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), "Oi, Dona Cida!"));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnConversationIdRequired_When_TheConversationIsEmpty()
    {
        // Act
        var result = _sut.Validate(Command(Guid.Empty, "Oi"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ConversationId" && e.ErrorMessage == ConversationsErrors.ConversationIdRequired.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Should_ReturnBodyRequired_When_TheTextIsBlank(string? body)
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), body));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Body" && e.ErrorMessage == ConversationsErrors.BodyRequired.Message);
    }

    [Fact]
    public void Validate_Should_ReturnBodyTooLong_When_TheTextIsLongerThanTheLimit()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), new string('a', Message.BodyMaxLength + 1)));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Body" && e.ErrorMessage == ConversationsErrors.BodyTooLong.Message);
    }

    [Fact]
    public void Validate_Should_IgnoreTheSurroundingSpaces_When_CountingTheLimit()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), $"  {new string('a', Message.BodyMaxLength)}  "));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("antes\0depois")]
    [InlineData("sino\u0007")]
    public void Validate_Should_ReturnBodyInvalidCharacters_When_TheTextHasAControlCharacter(string body)
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), body));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Body" && e.ErrorMessage == ConversationsErrors.BodyInvalidCharacters.Message);
    }

    [Fact]
    public void Validate_Should_Pass_When_TheTextHasLineBreaksAndTabs()
    {
        // Act
        var result = _sut.Validate(Command(TestIds.Of(1), "Linha 1\r\nLinha 2\n\tcom tab"));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    private static SendMessageCommand Command(Guid conversationId, string? body) =>
        new(ConversationBuilder.OwnerUserId, Roles.Owner, conversationId, body);
}
