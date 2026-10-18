using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews.UpdateReview;

/// <summary>Formato do que veio. Campo ausente não é validado (fica como está).</summary>
public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);

        RuleFor(x => x.Rating)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithMessage(ReviewsErrors.RatingInvalid.Message)
            .When(x => x.Rating is not null);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithMessage(ReviewsErrors.CommentTooLong.Message)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithMessage(ReviewsErrors.CommentInvalidCharacters.Message);

        RuleForEach(x => x.Pets).SetValidator(new ReviewPetInputValidator());
    }
}
