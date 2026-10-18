using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reviews;

public sealed class ReviewTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;

    // ----- Create -----

    [Fact]
    public void Create_Should_CopyTheStayAndTheContent_When_TheStayIsCompletedAndWithinTheWindow()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 4, "  Ótima casa.  ", null, Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var review = result.Value!;
        review.Id.Value.Should().NotBeEmpty();
        review.BookingId.Should().Be(ReviewBuilder.BookingId);
        review.OwnerId.Should().Be(ReviewBuilder.OwnerId);
        review.HostId.Should().Be(ReviewBuilder.HostId);
        review.ListingId.Should().Be(ReviewBuilder.ListingId);
        review.StayEndedAt.Should().Be(ReviewBuilder.CheckedOutAt);
        review.Rating.Should().Be(4);
        review.Comment.Should().Be("Ótima casa.");
        review.CreatedAt.Should().Be(Now);
        review.UpdatedAt.Should().Be(Now);
        review.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_StoreNoComment_When_TheCommentIsEmpty(string? comment)
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 5, comment, null, Now);

        // Assert
        result.Value!.Comment.Should().BeNull();
    }

    [Fact]
    public void Create_Should_ReturnBookingNotFound_When_TheStayBelongsToAnotherOwner()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, Guid.CreateVersion7(), 5, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotFound(ReviewBuilder.BookingId));
    }

    [Fact]
    public void Create_Should_ReturnBookingNotCompleted_When_TheStayIsNotCompleted()
    {
        // Arrange
        var stay = ReviewBuilder.CompletedStay() with { IsCompleted = false, CheckedOutAt = null };

        // Act
        var result = Review.Create(stay, ReviewParty.Owner, ReviewBuilder.OwnerId, 5, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotCompleted);
    }

    [Fact]
    public void Create_Should_ReturnBookingNotCompleted_When_TheCompletedStayHasNoCheckOutTime()
    {
        // Arrange
        var stay = ReviewBuilder.CompletedStay() with { CheckedOutAt = null };

        // Act
        var result = Review.Create(stay, ReviewParty.Owner, ReviewBuilder.OwnerId, 5, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotCompleted);
    }

    [Fact]
    public void Create_Should_Succeed_When_ItIsExactlySevenDaysAfterCheckOut()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 5, null, null, ReviewBuilder.CheckedOutAt.AddDays(Review.WindowDays));

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_ReturnWindowClosed_When_ItIsMoreThanSevenDaysAfterCheckOut()
    {
        // Act
        var result = Review.Create(
            ReviewBuilder.CompletedStay(),
            ReviewParty.Owner,
            ReviewBuilder.OwnerId,
            5,
            null,
            null,
            ReviewBuilder.CheckedOutAt.AddDays(Review.WindowDays).AddSeconds(1));

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.WindowClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Create_Should_ReturnRatingInvalid_When_TheRatingIsOutOfRange(int rating)
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, rating, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.RatingInvalid);
    }

    [Fact]
    public void Create_Should_ReturnCommentTooLong_When_TheCommentIsLongerThanTheLimit()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 5, new string('a', Review.CommentMaxLength + 1), null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.CommentTooLong);
    }

    [Fact]
    public void Create_Should_ReturnCommentInvalidCharacters_When_TheCommentHasAControlCharacter()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 5, "Boa\u0007casa", null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.CommentInvalidCharacters);
    }

    [Fact]
    public void Create_Should_KeepLineBreaksAndTabs_When_TheCommentHasThem()
    {
        // Act
        var result = Review.Create(ReviewBuilder.CompletedStay(), ReviewParty.Owner, ReviewBuilder.OwnerId, 5, "Linha 1\r\nLinha\t2", null, Now);

        // Assert
        result.Value!.Comment.Should().Be("Linha 1\r\nLinha\t2");
    }

    [Fact]
    public void Create_Should_Throw_When_TheStayIsNull()
    {
        // Act
        var act = () => Review.Create(null!, ReviewParty.Owner, ReviewBuilder.OwnerId, 5, null, null, Now);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ----- IsWrittenBy / EditableUntil -----

    [Fact]
    public void IsWrittenBy_Should_BeTrueOnlyForTheAuthor_When_Checked()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act & Assert
        review.IsWrittenBy(ReviewParty.Owner, ReviewBuilder.OwnerId).Should().BeTrue();
        review.IsWrittenBy(ReviewParty.Owner, Guid.CreateVersion7()).Should().BeFalse();
    }

    [Fact]
    public void EditableUntil_Should_BeSevenDaysAfterCheckOut_When_Read()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act & Assert
        review.EditableUntil.Should().Be(ReviewBuilder.CheckedOutAt.AddDays(7));
    }

    // ----- Update -----

    [Fact]
    public void Update_Should_ChangeTheRatingAndReportIt_When_ANewRatingIsSent()
    {
        // Arrange
        var review = new ReviewBuilder().WithRating(5).Build();
        var later = Now.AddHours(1);

        // Act
        var result = review.Update(3, null, null, null, later);

        // Assert
        result.Value.Should().Equal("rating");
        review.Rating.Should().Be(3);
        review.Comment.Should().Be("A Pipoca voltou feliz!");
        review.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Update_Should_ClearTheComment_When_AnEmptyCommentIsSent()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Update(null, "", null, null, Now.AddHours(1));

        // Assert
        result.Value.Should().Equal("comment");
        review.Comment.Should().BeNull();
    }

    [Fact]
    public void Update_Should_ReportBothFields_When_RatingAndCommentChange()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Update(2, " Mudei de ideia ", null, null, Now.AddHours(1));

        // Assert
        result.Value.Should().Equal("rating", "comment");
        review.Comment.Should().Be("Mudei de ideia");
    }

    [Fact]
    public void Update_Should_ChangeNothing_When_TheValuesAreTheSame()
    {
        // Arrange
        var review = new ReviewBuilder().WithRating(5).Build();

        // Act
        var result = review.Update(5, "A Pipoca voltou feliz!", null, null, Now.AddHours(1));

        // Assert
        result.Value.Should().BeEmpty();
        review.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_Should_ReturnWindowClosed_When_ItIsMoreThanSevenDaysAfterCheckOut()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Update(1, null, null, null, ReviewBuilder.CheckedOutAt.AddDays(8));

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.WindowClosed);
        review.Rating.Should().Be(5);
    }

    [Fact]
    public void Update_Should_ReturnRatingInvalid_When_TheNewRatingIsOutOfRange()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Update(9, null, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.RatingInvalid);
        review.Rating.Should().Be(5);
    }

    [Fact]
    public void Update_Should_ReturnCommentTooLong_When_TheNewCommentIsTooLong()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Update(null, new string('a', Review.CommentMaxLength + 1), null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.CommentTooLong);
    }

    // ----- Delete -----

    [Fact]
    public void Delete_Should_MarkTheReviewAsDeleted_When_Called()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        var later = Now.AddDays(30);

        // Act
        review.Delete(later);

        // Assert
        review.IsDeleted.Should().BeTrue();
        review.DeletedAt.Should().Be(later);
        review.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Delete_Should_KeepTheFirstDeletionTime_When_CalledTwice()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Delete(Now.AddDays(1));

        // Act
        review.Delete(Now.AddDays(2));

        // Assert
        review.DeletedAt.Should().Be(Now.AddDays(1));
    }

    // ----- Helpers -----

    [Fact]
    public void HasForbiddenControlCharacters_Should_Throw_When_TheTextIsNull()
    {
        // Act
        var act = () => Review.HasForbiddenControlCharacters(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
