using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;

namespace PetHost.Modules.Reviews.Application.Reviews.UnhideReview;

/// <summary>Só o id.</summary>
public sealed class UnhideReviewCommandValidator : AbstractValidator<UnhideReviewCommand>
{
    public UnhideReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);
    }
}
