using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reports;

public sealed class ReviewReportTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;
    private static readonly Guid ReporterUserId = ReviewBuilder.HostUserId;
    private static readonly Guid AdminId = Guid.Parse("0199d000-0000-7000-8000-0000000000aa");

    private static ReviewReport Pending() =>
        ReviewReport.Submit(new ReviewBuilder().Build(), ReviewParty.Host, ReviewBuilder.HostId, ReporterUserId, ReviewReportReason.Offensive, null, Now).Value!;

    // ----- Submit -----

    [Fact]
    public void Submit_Should_CreateAPendingReport_When_TheReviewedPartyReports()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = ReviewReport.Submit(review, ReviewParty.Host, ReviewBuilder.HostId, ReporterUserId, ReviewReportReason.PersonalData, "  Telefone no texto.  ", Now);

        // Assert
        var report = result.Value!;
        report.ReviewId.Should().Be(review.Id);
        report.ReportedBy.Should().Be(ReporterUserId);
        report.ReporterSide.Should().Be(ReviewParty.Host);
        report.Reason.Should().Be(ReviewReportReason.PersonalData);
        report.Description.Should().Be("Telefone no texto.");
        report.Status.Should().Be(ReviewReportStatus.Pending);
        report.IsPending.Should().BeTrue();
        report.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Submit_Should_ReturnReportOwnReview_When_TheAuthorReports()
    {
        // Act
        var result = ReviewReport.Submit(new ReviewBuilder().Build(), ReviewParty.Owner, ReviewBuilder.OwnerId, ReviewBuilder.OwnerUserId, ReviewReportReason.Spam, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportOwnReview);
    }

    [Fact]
    public void Submit_Should_ReturnDescriptionRequired_When_TheReasonIsOtherWithoutDescription()
    {
        // Act
        var result = ReviewReport.Submit(new ReviewBuilder().Build(), ReviewParty.Host, ReviewBuilder.HostId, ReporterUserId, ReviewReportReason.Other, " ", Now);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ReviewsErrors.DescriptionRequired);
    }

    [Fact]
    public void Submit_Should_ReturnBothDescriptionErrors_When_TheDescriptionIsTooLongAndHasControlCharacters()
    {
        // Arrange
        var description = new string('a', ReviewReport.TextMaxLength) + "\u0007";

        // Act
        var result = ReviewReport.Submit(new ReviewBuilder().Build(), ReviewParty.Host, ReviewBuilder.HostId, ReporterUserId, ReviewReportReason.Spam, description, Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.DescriptionTooLong, ReviewsErrors.DescriptionInvalidCharacters]);
    }

    [Fact]
    public void Submit_Should_Throw_When_TheReviewIsNull()
    {
        // Act
        var act = () => ReviewReport.Submit(null!, ReviewParty.Host, ReviewBuilder.HostId, ReporterUserId, ReviewReportReason.Spam, null, Now);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- Resolve -----

    [Theory]
    [InlineData(ReviewReportStatus.Upheld)]
    [InlineData(ReviewReportStatus.Dismissed)]
    public void Resolve_Should_CloseTheReport_When_ItIsPending(ReviewReportStatus outcome)
    {
        // Arrange
        var report = Pending();

        // Act
        var result = report.Resolve(outcome, AdminId, "  Analisado.  ", Now.AddDays(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        report.Status.Should().Be(outcome);
        report.ResolvedBy.Should().Be(AdminId);
        report.ResolvedAt.Should().Be(Now.AddDays(1));
        report.ResolutionNote.Should().Be("Analisado.");
    }

    [Fact]
    public void Resolve_Should_ReturnAlreadyResolved_When_ItWasAlreadyResolved()
    {
        // Arrange
        var report = Pending();
        report.Resolve(ReviewReportStatus.Dismissed, AdminId, "Ok", Now);

        // Act
        var result = report.Resolve(ReviewReportStatus.Upheld, AdminId, "Ok", Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportAlreadyResolved);
    }

    [Fact]
    public void Resolve_Should_ReturnNoteRequired_When_TheNoteIsEmpty()
    {
        // Act
        var result = Pending().Resolve(ReviewReportStatus.Upheld, AdminId, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NoteRequired);
    }

    [Fact]
    public void Resolve_Should_ReturnBothNoteErrors_When_TheNoteIsTooLongAndHasControlCharacters()
    {
        // Act
        var result = Pending().Resolve(ReviewReportStatus.Upheld, AdminId, new string('a', ReviewReport.TextMaxLength) + "\u0007", Now);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.NoteTooLong, ReviewsErrors.NoteInvalidCharacters]);
    }

    [Fact]
    public void Resolve_Should_Throw_When_TheOutcomeIsPending()
    {
        // Act
        var act = () => Pending().Resolve(ReviewReportStatus.Pending, AdminId, "Ok", Now);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Resolve_Should_Throw_When_TheAdminIsEmpty()
    {
        // Act
        var act = () => Pending().Resolve(ReviewReportStatus.Upheld, Guid.Empty, "Ok", Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    // ----- Valores -----

    [Theory]
    [InlineData(ReviewReportReason.Offensive, "offensive")]
    [InlineData(ReviewReportReason.FalseInformation, "false_information")]
    [InlineData(ReviewReportReason.PersonalData, "personal_data")]
    [InlineData(ReviewReportReason.Spam, "spam")]
    [InlineData(ReviewReportReason.Other, "other")]
    public void ReasonValues_Should_ConvertBothWays_When_Known(ReviewReportReason reason, string wire)
    {
        // Act & Assert
        ReviewReportReasonValues.ToWire(reason).Should().Be(wire);
        ReviewReportReasonValues.FromWire(wire).Should().Be(reason);
    }

    [Theory]
    [InlineData(ReviewReportStatus.Pending, "pending")]
    [InlineData(ReviewReportStatus.Upheld, "upheld")]
    [InlineData(ReviewReportStatus.Dismissed, "dismissed")]
    public void StatusValues_Should_ConvertBothWays_When_Known(ReviewReportStatus status, string wire)
    {
        // Act & Assert
        ReviewReportStatusValues.ToWire(status).Should().Be(wire);
        ReviewReportStatusValues.FromWire(wire).Should().Be(status);
    }

    [Fact]
    public void Values_Should_RejectUnknownText_When_Parsed()
    {
        // Act & Assert
        ReviewReportReasonValues.TryFromWire("Spam", out _).Should().BeFalse();
        ReviewReportStatusValues.TryOutcomeFromWire("pending", out _).Should().BeFalse();
        ReviewReportStatusValues.TryOutcomeFromWire("upheld", out _).Should().BeTrue();
        ((Action)(() => ReviewReportReasonValues.FromWire("x"))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReviewReportStatusValues.FromWire("x"))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReviewReportReasonValues.ToWire((ReviewReportReason)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => ReviewReportStatusValues.ToWire((ReviewReportStatus)99))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- Lembrete -----

    [Fact]
    public void ReviewReminder_Should_KeepTheBookingAndSide_When_Created()
    {
        // Act
        var reminder = new ReviewReminder(ReviewBuilder.BookingId, ReviewParty.Host, Now);

        // Assert
        reminder.BookingId.Should().Be(ReviewBuilder.BookingId);
        reminder.Side.Should().Be(ReviewParty.Host);
        reminder.SentAt.Should().Be(Now);
    }

    [Fact]
    public void ReviewReminder_Should_Throw_When_TheBookingIsEmpty()
    {
        // Act
        var act = () => new ReviewReminder(Guid.Empty, ReviewParty.Owner, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReviewReportId_Should_BeUniqueAndPrintTheGuid_When_Created()
    {
        // Act
        var id = ReviewReportId.New();

        // Assert
        id.Should().NotBe(ReviewReportId.New());
        id.ToString().Should().Be(id.Value.ToString());
    }
}
