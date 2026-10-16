using FluentAssertions;
using PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports.ResolveMessageReport;

public sealed class ResolveMessageReportCommandValidatorTests
{
    private readonly ResolveMessageReportCommandValidator _sut = new();

    [Theory]
    [InlineData("upheld")]
    [InlineData("dismissed")]
    public void Validate_Should_Pass_When_TheOutcomeAndNoteAreValid(string outcome)
    {
        // Act
        var result = _sut.Validate(Command(outcome, "Conferido com a conversa."));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnReportIdRequired_When_TheReportIsEmpty()
    {
        // Act
        var result = _sut.Validate(new ResolveMessageReportCommand(TestIds.Of(1), Guid.Empty, "upheld", "Nota"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.ReportIdRequired.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pending")]
    [InlineData("closed")]
    public void Validate_Should_ReturnOutcomeInvalid_When_TheOutcomeIsNotUpheldOrDismissed(string? outcome)
    {
        // Act
        var result = _sut.Validate(Command(outcome, "Nota"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Outcome" && e.ErrorMessage == ConversationsErrors.OutcomeInvalid.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validate_Should_ReturnNoteRequired_When_TheNoteIsBlank(string? note)
    {
        // Act
        var result = _sut.Validate(Command("upheld", note));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Note" && e.ErrorMessage == ConversationsErrors.NoteRequired.Message);
    }

    [Fact]
    public void Validate_Should_ReturnNoteTooLong_When_TheNoteIsLongerThanTheLimit()
    {
        // Act
        var result = _sut.Validate(Command("upheld", new string('a', MessageReport.ResolutionNoteMaxLength + 1)));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.NoteTooLong.Message);
    }

    [Fact]
    public void Validate_Should_ReturnNoteInvalidCharacters_When_TheNoteHasAControlCharacter()
    {
        // Act
        var result = _sut.Validate(Command("dismissed", "nota\u0007"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ConversationsErrors.NoteInvalidCharacters.Message);
    }

    private static ResolveMessageReportCommand Command(string? outcome, string? note) =>
        new(TestIds.Of(1), TestIds.Of(2), outcome, note);
}
