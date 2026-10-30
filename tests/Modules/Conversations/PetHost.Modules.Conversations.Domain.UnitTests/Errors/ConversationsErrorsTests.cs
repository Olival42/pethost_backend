using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
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

    [Fact]
    public void MessageNotFound_Should_UseTheModuleCodeAndNameTheMessage_When_Built()
    {
        // Arrange
        var id = new MessageId(Guid.Parse("0199c000-0000-7000-8000-000000000041"));

        // Act
        var error = ConversationsErrors.MessageNotFound(id);

        // Assert
        error.Code.Should().Be("CONVERSATION_MESSAGE_NOT_FOUND");
        error.Message.Should().Contain(id.Value.ToString());
    }

    [Fact]
    public void ReportNotFound_Should_UseTheModuleCodeAndNameTheReport_When_Built()
    {
        // Arrange
        var id = new MessageReportId(Guid.Parse("0199c000-0000-7000-8000-000000000051"));

        // Act
        var error = ConversationsErrors.ReportNotFound(id);

        // Assert
        error.Code.Should().Be("CONVERSATION_REPORT_NOT_FOUND");
        error.Message.Should().Contain(id.Value.ToString());
    }

    [Theory]
    [InlineData("CONVERSATION_OWNER_NOT_FOUND", "OwnerNotFound")]
    [InlineData("CONVERSATION_HOST_NOT_FOUND", "HostNotFound")]
    [InlineData("CONVERSATION_OWNER_DEACTIVATED", "OwnerDeactivated")]
    [InlineData("CONVERSATION_HOST_DEACTIVATED", "HostDeactivated")]
    [InlineData("CONVERSATION_RECIPIENT_SUSPENDED", "RecipientSuspended")]
    [InlineData("CONVERSATION_ALREADY_EXISTS", "AlreadyExists")]
    [InlineData("CONVERSATION_CONFLICT", "Conflict")]
    [InlineData("CONVERSATION_BLOCKED", "Blocked")]
    [InlineData("CONVERSATION_BLOCKED_BY_MODERATION", "BlockedByModeration")]
    [InlineData("CONVERSATION_ALREADY_BLOCKED", "AlreadyBlocked")]
    [InlineData("CONVERSATION_UNBLOCK_FORBIDDEN", "UnblockForbidden")]
    [InlineData("CONVERSATION_REPORT_OWN_MESSAGE", "ReportOwnMessage")]
    [InlineData("CONVERSATION_REPORT_ALREADY_EXISTS", "ReportAlreadyExists")]
    [InlineData("CONVERSATION_REPORT_ALREADY_RESOLVED", "ReportAlreadyResolved")]
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
    [InlineData("MessageIdRequired", "messageId")]
    [InlineData("ReportIdRequired", "reportId")]
    [InlineData("ReasonInvalid", "reason")]
    [InlineData("DescriptionRequired", "description")]
    [InlineData("DescriptionTooLong", "description")]
    [InlineData("DescriptionInvalidCharacters", "description")]
    [InlineData("OutcomeInvalid", "outcome")]
    [InlineData("NoteRequired", "note")]
    [InlineData("NoteTooLong", "note")]
    [InlineData("NoteInvalidCharacters", "note")]
    [InlineData("StatusInvalid", "status")]
    [InlineData("UnreadMessagesEmailRequired", "unreadMessagesEmail")]
    [InlineData("UnreadDelayInvalid", "delayMinutes")]
    [InlineData("UnreadCooldownInvalid", "cooldownMinutes")]
    public void ValidationErrors_Should_PointToTheJsonField_When_Read(string field, string expectedJsonField)
    {
        // Act
        var error = (Error)typeof(ConversationsErrors).GetField(field)!.GetValue(null)!;

        // Assert
        error.Code.Should().Be(ErrorCodes.Validation);
        error.Field.Should().Be(expectedJsonField);
    }
}
