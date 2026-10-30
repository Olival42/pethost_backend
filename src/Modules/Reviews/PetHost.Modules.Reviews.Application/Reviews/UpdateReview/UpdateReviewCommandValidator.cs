using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reviews.UpdateReview;

/// <summary>Formato do que veio. Campo ausente não é validado (fica como está).</summary>
public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithError(ReviewsErrors.ReviewIdRequired);

        RuleFor(x => x.Rating)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithError(ReviewsErrors.RatingInvalid)
            .When(x => x.Rating is not null);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithError(ReviewsErrors.CommentTooLong)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithError(ReviewsErrors.CommentInvalidCharacters);

        RuleForEach(x => x.Pets).SetValidator(new ReviewPetInputValidator());
    }
}
