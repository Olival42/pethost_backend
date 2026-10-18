using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews.CreateReview;

/// <summary>Formato da avaliação. Reserva, prazo, pets e duplicidade são conferidos pelo handler e pelo agregado.</summary>
public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage(ReviewsErrors.BookingIdRequired.Message);

        RuleFor(x => x.Rating)
            .NotNull().WithMessage(ReviewsErrors.RatingRequired.Message)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithMessage(ReviewsErrors.RatingInvalid.Message);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithMessage(ReviewsErrors.CommentTooLong.Message)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithMessage(ReviewsErrors.CommentInvalidCharacters.Message);

        RuleForEach(x => x.Pets).SetValidator(new ReviewPetInputValidator());
    }
}
