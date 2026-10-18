using FluentAssertions;
using PetHost.Modules.Reviews.Application.Reports.ReportReview;
using PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Application.Reviews.CreateReview;
using PetHost.Modules.Reviews.Application.Reviews.DeleteReview;
using PetHost.Modules.Reviews.Application.Reviews.HideReview;
using PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;
using PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;
using PetHost.Modules.Reviews.Application.Reviews.UnhideReview;
using PetHost.Modules.Reviews.Application.Reviews.UpdateReview;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews;

/// <summary>Uma asserção por regra de cada validador (§14).</summary>
public sealed class ReviewValidatorsTests
{
    private static readonly Guid ReviewId = Guid.CreateVersion7();
    private static readonly Guid User = ReviewBuilder.OwnerUserId;
    private static readonly string TooLong = new('a', Review.CommentMaxLength + 1);

    // ----- Create -----

    private static CreateReviewCommand Create(Guid? bookingId = null, int? rating = 5, string? comment = null, IReadOnlyList<ReviewPetInput>? pets = null) =>
        new(User, Roles.Owner, bookingId ?? ReviewBuilder.BookingId, rating, comment, pets);

    [Fact]
    public void Create_Should_Pass_When_TheCommandIsValid()
    {
        // Act & Assert
        new CreateReviewCommandValidator()
            .Validate(Create(comment: "Linha 1\nLinha 2", pets: [new ReviewPetInput(ReviewBuilder.PetId, 5, null)]))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("BookingId")]
    [InlineData("Rating")]
    public void Create_Should_RequireTheField_When_ItIsMissing(string property)
    {
        // Arrange
        var command = property == "BookingId" ? Create(bookingId: Guid.Empty) : Create(rating: null);

        // Act
        var result = new CreateReviewCommandValidator().Validate(command);

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == property);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_Should_ReturnRatingInvalid_When_TheRatingIsOutOfRange(int rating)
    {
        // Act
        var result = new CreateReviewCommandValidator().Validate(Create(rating: rating));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Rating" && e.ErrorMessage == ReviewsErrors.RatingInvalid.Message);
    }

    [Fact]
    public void Create_Should_ReturnCommentTooLong_When_TheCommentIsTooLong()
    {
        // Act
        var result = new CreateReviewCommandValidator().Validate(Create(comment: TooLong));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Comment" && e.ErrorMessage == ReviewsErrors.CommentTooLong.Message);
    }

    [Fact]
    public void Create_Should_ReturnCommentInvalidCharacters_When_TheCommentHasAControlCharacter()
    {
        // Act
        var result = new CreateReviewCommandValidator().Validate(Create(comment: "a\u0000b"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Comment" && e.ErrorMessage == ReviewsErrors.CommentInvalidCharacters.Message);
    }

    [Fact]
    public void Create_Should_ValidateEachPet_When_PetsAreSent()
    {
        // Act
        var result = new CreateReviewCommandValidator().Validate(Create(pets: [new ReviewPetInput(Guid.Empty, null, TooLong)]));

        // Assert
        result.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(
        [
            ReviewsErrors.PetIdRequired.Message, ReviewsErrors.PetRatingInvalid.Message, ReviewsErrors.PetCommentTooLong.Message,
        ]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void PetInput_Should_ReturnPetRatingInvalid_When_TheRatingIsOutOfRange(int rating)
    {
        // Act
        var result = new ReviewPetInputValidator().Validate(new ReviewPetInput(ReviewBuilder.PetId, rating, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.PetRatingInvalid.Message);
    }

    [Fact]
    public void PetInput_Should_ReturnPetCommentInvalidCharacters_When_TheCommentHasAControlCharacter()
    {
        // Act
        var result = new ReviewPetInputValidator().Validate(new ReviewPetInput(ReviewBuilder.PetId, 5, "a\u0007"));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.PetCommentInvalidCharacters.Message);
    }

    [Fact]
    public void PetInput_Should_MapToTheDomain_When_Converted()
    {
        // Act & Assert
        ReviewPetInput.ToDomain(null).Should().BeNull();
        ReviewPetInput.ToDomain([new ReviewPetInput(ReviewBuilder.PetId, 3, "Ok")]).Should().Equal(new PetRatingInput(ReviewBuilder.PetId, 3, "Ok"));
    }

    // ----- Update -----

    [Fact]
    public void Update_Should_Pass_When_NothingIsSent()
    {
        // Act & Assert
        new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Owner, ReviewId, null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_Should_ReturnReviewIdRequired_When_TheIdIsEmpty()
    {
        // Act
        var result = new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Owner, Guid.Empty, null, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
    }

    [Fact]
    public void Update_Should_ReturnRatingInvalid_When_TheRatingIsOutOfRange()
    {
        // Act
        var result = new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Owner, ReviewId, 7, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Rating" && e.ErrorMessage == ReviewsErrors.RatingInvalid.Message);
    }

    [Fact]
    public void Update_Should_ReturnCommentErrors_When_TheCommentIsInvalid()
    {
        // Act
        var tooLong = new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Owner, ReviewId, null, TooLong));
        var control = new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Owner, ReviewId, null, "a\u001bb"));

        // Assert
        tooLong.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.CommentTooLong.Message);
        control.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.CommentInvalidCharacters.Message);
    }

    [Fact]
    public void Update_Should_ValidateEachPet_When_PetsAreSent()
    {
        // Act
        var result = new UpdateReviewCommandValidator().Validate(new UpdateReviewCommand(User, Roles.Host, ReviewId, null, null, [new ReviewPetInput(ReviewBuilder.PetId, 9, null)]));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.PetRatingInvalid.Message);
    }

    // ----- Só o id -----

    [Fact]
    public void IdOnlyValidators_Should_RequireTheId_When_ItIsEmpty()
    {
        // Act & Assert
        new DeleteReviewCommandValidator().Validate(new DeleteReviewCommand(User, Roles.Owner, Guid.Empty)).Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
        new RemoveReviewReplyCommandValidator().Validate(new RemoveReviewReplyCommand(User, Roles.Host, Guid.Empty)).Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
        new UnhideReviewCommandValidator().Validate(new UnhideReviewCommand(User, Guid.Empty)).Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
    }

    [Fact]
    public void IdOnlyValidators_Should_Pass_When_TheIdIsInformed()
    {
        // Act & Assert
        new DeleteReviewCommandValidator().Validate(new DeleteReviewCommand(User, Roles.Owner, ReviewId)).IsValid.Should().BeTrue();
        new RemoveReviewReplyCommandValidator().Validate(new RemoveReviewReplyCommand(User, Roles.Host, ReviewId)).IsValid.Should().BeTrue();
        new UnhideReviewCommandValidator().Validate(new UnhideReviewCommand(User, ReviewId)).IsValid.Should().BeTrue();
    }

    // ----- Reply -----

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Reply_Should_ReturnReplyRequired_When_TheTextIsEmpty(string? text)
    {
        // Act
        var result = new ReplyToReviewCommandValidator().Validate(new ReplyToReviewCommand(User, Roles.Host, ReviewId, text));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.ReplyRequired.Message);
    }

    [Fact]
    public void Reply_Should_ReturnTextErrors_When_TheTextIsInvalid()
    {
        // Act
        var tooLong = new ReplyToReviewCommandValidator().Validate(new ReplyToReviewCommand(User, Roles.Host, ReviewId, TooLong));
        var control = new ReplyToReviewCommandValidator().Validate(new ReplyToReviewCommand(User, Roles.Host, ReviewId, "a\u0007"));
        var noId = new ReplyToReviewCommandValidator().Validate(new ReplyToReviewCommand(User, Roles.Host, Guid.Empty, "Oi"));

        // Assert
        tooLong.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.ReplyTooLong.Message);
        control.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.ReplyInvalidCharacters.Message);
        noId.Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
    }

    // ----- Report -----

    [Fact]
    public void Report_Should_Pass_When_TheReasonIsKnown()
    {
        // Act & Assert
        new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, ReviewId, ReviewReportReasonValues.Spam, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Report_Should_ReturnReasonInvalid_When_TheReasonIsUnknown()
    {
        // Act
        var result = new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, ReviewId, "rude", null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.ReasonInvalid.Message);
    }

    [Fact]
    public void Report_Should_ReturnDescriptionRequired_When_TheReasonIsOtherWithoutDescription()
    {
        // Act
        var result = new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, ReviewId, ReviewReportReasonValues.Other, " "));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.DescriptionRequired.Message);
    }

    [Fact]
    public void Report_Should_ReturnDescriptionErrors_When_TheDescriptionIsInvalid()
    {
        // Act
        var tooLong = new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, ReviewId, ReviewReportReasonValues.Spam, TooLong));
        var control = new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, ReviewId, ReviewReportReasonValues.Spam, "a\u0007"));
        var noId = new ReportReviewCommandValidator().Validate(new ReportReviewCommand(User, Roles.Host, Guid.Empty, ReviewReportReasonValues.Spam, null));

        // Assert
        tooLong.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.DescriptionTooLong.Message);
        control.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.DescriptionInvalidCharacters.Message);
        noId.Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
    }

    // ----- Resolve -----

    [Fact]
    public void Resolve_Should_Pass_When_TheOutcomeAndNoteAreValid()
    {
        // Act & Assert
        new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, ReviewId, "upheld", "Ofensivo.")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("closed")]
    [InlineData("approved")]
    [InlineData(null)]
    public void Resolve_Should_ReturnOutcomeInvalid_When_TheOutcomeIsNotAResolution(string? outcome)
    {
        // Act
        var result = new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, ReviewId, outcome, "Ok."));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.OutcomeInvalid.Message);
    }

    [Fact]
    public void Resolve_Should_ReturnNoteErrors_When_TheNoteIsInvalid()
    {
        // Act
        var empty = new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, ReviewId, "upheld", null));
        var tooLong = new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, ReviewId, "upheld", TooLong));
        var control = new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, ReviewId, "upheld", "a\u0007"));
        var noId = new ResolveReviewReportCommandValidator().Validate(new ResolveReviewReportCommand(User, Guid.Empty, "upheld", "Ok."));

        // Assert
        empty.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.NoteRequired.Message);
        tooLong.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.NoteTooLong.Message);
        control.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.NoteInvalidCharacters.Message);
        noId.Errors.Should().ContainSingle(e => e.PropertyName == "ReportId");
    }

    // ----- Ocultar direto -----

    [Fact]
    public void Hide_Should_Pass_When_TheIdAndReasonAreInformed()
    {
        // Act & Assert
        new HideReviewCommandValidator().Validate(new HideReviewCommand(User, ReviewId, "Dados pessoais.")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Hide_Should_ReturnReasonErrors_When_TheReasonIsInvalid()
    {
        // Act
        var empty = new HideReviewCommandValidator().Validate(new HideReviewCommand(User, ReviewId, "   "));
        var tooLong = new HideReviewCommandValidator().Validate(new HideReviewCommand(User, ReviewId, TooLong));
        var control = new HideReviewCommandValidator().Validate(new HideReviewCommand(User, ReviewId, "a\u0007"));
        var noId = new HideReviewCommandValidator().Validate(new HideReviewCommand(User, Guid.Empty, "Ok."));

        // Assert
        empty.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.HideReasonRequired.Message);
        tooLong.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.HideReasonTooLong.Message);
        control.Errors.Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.HideReasonInvalidCharacters.Message);
        noId.Errors.Should().ContainSingle(e => e.PropertyName == "ReviewId");
    }
}
