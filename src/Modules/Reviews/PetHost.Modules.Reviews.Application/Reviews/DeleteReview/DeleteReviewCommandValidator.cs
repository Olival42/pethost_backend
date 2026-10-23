using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reviews.DeleteReview;

/// <summary>Só o id. Se a avaliação é de quem pede é conferido pelo handler.</summary>
public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithError(ReviewsErrors.ReviewIdRequired);
    }
}
