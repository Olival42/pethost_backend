using FluentAssertions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Domain.UnitTests.Reports;

public sealed class MessageReportTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddHours(1);
    private static readonly Guid AdminId = TestIds.Of(900);

    private readonly Conversation _conversation = new ConversationBuilder().Build();

    [Fact]
    public void Submit_Should_CreateAPendingReport_When_TheRecipientReports()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Host);

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Owner, ReportReason.Scam, "  Pediu Pix por fora.  ", Now);

        // Assert
        var report = result.Value!;
        report.Id.Value.Version.Should().Be(7);
        report.MessageId.Should().Be(message.Id);
        report.ConversationId.Should().Be(_conversation.Id);
        report.ReportedBy.Should().Be(ConversationParty.Owner);
        report.Reason.Should().Be(ReportReason.Scam);
        report.Description.Should().Be("Pediu Pix por fora.");
        report.Status.Should().Be(ReportStatus.Pending);
        report.IsPending.Should().BeTrue();
        report.CreatedAt.Should().Be(Now);
        report.ResolvedAt.Should().BeNull();
        report.ResolvedBy.Should().BeNull();
        report.ResolutionNote.Should().BeNull();
    }

    [Fact]
    public void Submit_Should_AcceptNoDescription_When_TheReasonIsNotOther()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Owner);

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Host, ReportReason.Spam, "   ", Now);

        // Assert
        result.Value!.Description.Should().BeNull();
    }

    [Fact]
    public void Submit_Should_Fail_When_TheSenderReportsTheirOwnMessage()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Owner);

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Owner, ReportReason.Spam, null, Now);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportOwnMessage);
    }

    [Fact]
    public void Submit_Should_RequireADescription_When_TheReasonIsOther()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Host);

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Owner, ReportReason.Other, " ", Now);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.DescriptionRequired);
    }

    [Fact]
    public void Submit_Should_AcceptTheMaximumLength_When_TheDescriptionIsExactlyAtTheLimit()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Host);

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Owner, ReportReason.Other, new string('a', MessageReport.DescriptionMaxLength), Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Submit_Should_ReturnBothErrors_When_TheDescriptionIsTooLongAndHasControlCharacters()
    {
        // Arrange
        var message = ConversationBuilder.Text(_conversation, ConversationParty.Host);
        var description = new string('a', MessageReport.DescriptionMaxLength) + "\0";

        // Act
        var result = MessageReport.Submit(message, ConversationParty.Owner, ReportReason.Harassment, description, Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ConversationsErrors.DescriptionTooLong, ConversationsErrors.DescriptionInvalidCharacters]);
    }

    [Theory]
    [InlineData(ReportStatus.Upheld)]
    [InlineData(ReportStatus.Dismissed)]
    public void Resolve_Should_CloseTheReport_When_ItIsPending(ReportStatus outcome)
    {
        // Arrange
        var report = Pending();

        // Act
        var result = report.Resolve(outcome, AdminId, "  Conferido com a conversa.  ", Now.AddDays(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        report.Status.Should().Be(outcome);
        report.IsPending.Should().BeFalse();
        report.ResolvedAt.Should().Be(Now.AddDays(1));
        report.ResolvedBy.Should().Be(AdminId);
        report.ResolutionNote.Should().Be("Conferido com a conversa.");
    }

    [Fact]
    public void Resolve_Should_Fail_When_TheReportWasAlreadyResolved()
    {
        // Arrange
        var report = Pending();
        report.Resolve(ReportStatus.Dismissed, AdminId, "Sem problema.", Now);

        // Act
        var result = report.Resolve(ReportStatus.Upheld, AdminId, "Mudei de ideia.", Now.AddHours(1));

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportAlreadyResolved);
        report.Status.Should().Be(ReportStatus.Dismissed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resolve_Should_RequireANote_When_TheNoteIsMissing(string? note)
    {
        // Arrange
        var report = Pending();

        // Act
        var result = report.Resolve(ReportStatus.Upheld, AdminId, note, Now);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NoteRequired);
        report.IsPending.Should().BeTrue();
    }

    [Fact]
    public void Resolve_Should_ReturnBothErrors_When_TheNoteIsTooLongAndHasControlCharacters()
    {
        // Arrange
        var report = Pending();
        var note = new string('a', MessageReport.ResolutionNoteMaxLength) + "\u0007";

        // Act
        var result = report.Resolve(ReportStatus.Upheld, AdminId, note, Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ConversationsErrors.NoteTooLong, ConversationsErrors.NoteInvalidCharacters]);
    }

    [Fact]
    public void Resolve_Should_Throw_When_TheOutcomeIsPending()
    {
        // Arrange
        var report = Pending();

        // Act
        var resolve = () => report.Resolve(ReportStatus.Pending, AdminId, "Nota", Now);

        // Assert
        resolve.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("outcome");
    }

    [Fact]
    public void Resolve_Should_Throw_When_TheAdminIdIsEmpty()
    {
        // Arrange
        var report = Pending();

        // Act
        var resolve = () => report.Resolve(ReportStatus.Upheld, Guid.Empty, "Nota", Now);

        // Assert
        resolve.Should().Throw<ArgumentException>().WithParameterName("adminUserId");
    }

    private MessageReport Pending() =>
        MessageReport.Submit(ConversationBuilder.Text(_conversation, ConversationParty.Host), ConversationParty.Owner, ReportReason.Spam, null, Now).Value!;
}
