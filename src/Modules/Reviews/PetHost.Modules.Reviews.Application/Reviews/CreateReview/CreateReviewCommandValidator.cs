using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reviews.CreateReview;

/// <summary>Formato da avaliação. Reserva, prazo, pets e duplicidade são conferidos pelo handler e pelo agregado.</summary>
public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithError(ReviewsErrors.BookingIdRequired);

        RuleFor(x => x.Rating)
            .NotNull().WithError(ReviewsErrors.RatingRequired)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithError(ReviewsErrors.RatingInvalid);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithError(ReviewsErrors.CommentTooLong)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithError(ReviewsErrors.CommentInvalidCharacters);

        RuleForEach(x => x.Pets).SetValidator(new ReviewPetInputValidator());
    }
}
