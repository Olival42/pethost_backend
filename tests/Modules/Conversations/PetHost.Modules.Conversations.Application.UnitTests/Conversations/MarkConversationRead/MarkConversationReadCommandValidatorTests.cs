using FluentAssertions;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.MarkConversationRead;

public sealed class MarkConversationReadCommandValidatorTests
{
    private readonly MarkConversationReadCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_TheConversationIsInformed()
    {
        // Act
        var result = _sut.Validate(new MarkConversationReadCommand(ConversationBuilder.OwnerUserId, Roles.Owner, TestIds.Of(1)));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnConversationIdRequired_When_TheConversationIsEmpty()
    {
        // Act
        var result = _sut.Validate(new MarkConversationReadCommand(ConversationBuilder.OwnerUserId, Roles.Owner, Guid.Empty));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ConversationId" && e.ErrorMessage == ConversationsErrors.ConversationIdRequired.Message);
    }
}
