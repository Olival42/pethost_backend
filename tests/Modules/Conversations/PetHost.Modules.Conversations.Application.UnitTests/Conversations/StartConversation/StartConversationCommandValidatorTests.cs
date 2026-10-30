using FluentAssertions;
using PetHost.Modules.Conversations.Application.Conversations.StartConversation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.StartConversation;

public sealed class StartConversationCommandValidatorTests
{
    private readonly StartConversationCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_TheListingIsInformed()
    {
        // Act
        var result = _sut.Validate(new StartConversationCommand(ConversationBuilder.OwnerUserId, ConversationBuilder.ListingId));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnListingIdRequired_When_TheListingIsEmpty()
    {
        // Act
        var result = _sut.Validate(new StartConversationCommand(ConversationBuilder.OwnerUserId, Guid.Empty));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ListingId" && e.ErrorMessage == ConversationsErrors.ListingIdRequired.Message);
    }
}
