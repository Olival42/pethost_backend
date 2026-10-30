using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Errors;

public sealed class ReviewsErrorsTests
{
    [Fact]
    public void NotFound_Should_CarryTheIdInTheMessage_When_Created()
    {
        // Arrange
        var id = ReviewId.New();

        // Act
        var error = ReviewsErrors.NotFound(id);

        // Assert
        error.Code.Should().Be("REVIEW_NOT_FOUND");
        error.Message.Should().Contain(id.Value.ToString());
    }

    [Fact]
    public void BookingNotFound_Should_CarryTheIdInTheMessage_When_Created()
    {
        // Arrange
        var id = Guid.CreateVersion7();

        // Act
        var error = ReviewsErrors.BookingNotFound(id);

        // Assert
        error.Code.Should().Be("REVIEW_BOOKING_NOT_FOUND");
        error.Message.Should().Contain(id.ToString());
    }

    [Fact]
    public void ValidationErrors_Should_UseTheValidationCodeWithAField_When_Declared()
    {
        // Arrange
        Error[] errors =
        [
            ReviewsErrors.BookingIdRequired, ReviewsErrors.ReviewIdRequired, ReviewsErrors.RatingRequired,
            ReviewsErrors.RatingInvalid, ReviewsErrors.CommentTooLong, ReviewsErrors.CommentInvalidCharacters,
            ReviewsErrors.HostIdInvalid, ReviewsErrors.ListingIdInvalid, ReviewsErrors.PageInvalid, ReviewsErrors.PageSizeInvalid,
        ];

        // Act & Assert
        errors.Should().OnlyContain(e => e.Code == ErrorCodes.Validation && !string.IsNullOrEmpty(e.Field));
    }

    [Fact]
    public void BusinessErrors_Should_UseModuleCodes_When_Declared()
    {
        // Arrange
        Error[] errors =
        [
            ReviewsErrors.OwnerNotFound, ReviewsErrors.OwnerDeactivated, ReviewsErrors.BookingNotCompleted,
            ReviewsErrors.WindowClosed, ReviewsErrors.AlreadyExists, ReviewsErrors.Conflict,
        ];

        // Act & Assert
        errors.Select(e => e.Code).Should().Equal(
            "REVIEW_OWNER_NOT_FOUND", "REVIEW_OWNER_DEACTIVATED", "REVIEW_BOOKING_NOT_COMPLETED",
            "REVIEW_WINDOW_CLOSED", "REVIEW_ALREADY_EXISTS", "REVIEW_CONFLICT");
    }
}
