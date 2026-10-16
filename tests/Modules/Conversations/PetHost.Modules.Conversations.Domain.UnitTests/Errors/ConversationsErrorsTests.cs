using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Errors;

/// <summary>Os códigos são contrato público (§8): mudar um quebra o app. Estes testes travam os nomes.</summary>
public sealed class ConversationsErrorsTests
{
    [Fact]
    public void NotFound_Should_UseTheModuleCodeAndNameTheId_When_Built()
    {
        // Arrange
        var id = new ConversationId(Guid.Parse("0199c000-0000-7000-8000-000000000099"));

        // Act
        var error = ConversationsErrors.NotFound(id);

        // Assert
        error.Code.Should().Be("CONVERSATION_NOT_FOUND");
        error.Message.Should().Contain("0199c000-0000-7000-8000-000000000099");
    }

    [Fact]
    public void ListingNotFound_Should_UseTheModuleCodeAndNameTheListing_When_Built()
    {
        // Arrange
        var listingId = Guid.Parse("0199c000-0000-7000-8000-000000000021");

        // Act
        var error = ConversationsErrors.ListingNotFound(listingId);

        // Assert
        error.Code.Should().Be("CONVERSATION_LISTING_NOT_FOUND");
        error.Message.Should().Contain(listingId.ToString());
    }

    [Theory]
    [InlineData("CONVERSATION_OWNER_NOT_FOUND", "OwnerNotFound")]
    [InlineData("CONVERSATION_HOST_NOT_FOUND", "HostNotFound")]
    [InlineData("CONVERSATION_OWNER_DEACTIVATED", "OwnerDeactivated")]
    [InlineData("CONVERSATION_HOST_DEACTIVATED", "HostDeactivated")]
    [InlineData("CONVERSATION_RECIPIENT_SUSPENDED", "RecipientSuspended")]
    [InlineData("CONVERSATION_ALREADY_EXISTS", "AlreadyExists")]
    [InlineData("CONVERSATION_CONFLICT", "Conflict")]
    public void BusinessErrors_Should_KeepTheirPublicCodes_When_Read(string expectedCode, string field)
    {
        // Act
        var error = (Error)typeof(ConversationsErrors).GetField(field)!.GetValue(null)!;

        // Assert
        error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("ListingIdRequired", "listingId")]
    [InlineData("ConversationIdRequired", "conversationId")]
    [InlineData("BodyRequired", "body")]
    [InlineData("BodyTooLong", "body")]
    [InlineData("BodyInvalidCharacters", "body")]
    [InlineData("PageInvalid", "page")]
    [InlineData("PageSizeInvalid", "pageSize")]
    [InlineData("BeforeInvalid", "before")]
    public void ValidationErrors_Should_PointToTheJsonField_When_Read(string field, string expectedJsonField)
    {
        // Act
        var error = (Error)typeof(ConversationsErrors).GetField(field)!.GetValue(null)!;

        // Assert
        error.Code.Should().Be(ErrorCodes.Validation);
        error.Field.Should().Be(expectedJsonField);
    }
}
