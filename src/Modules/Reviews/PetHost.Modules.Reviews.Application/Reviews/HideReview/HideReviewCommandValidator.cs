using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reviews.HideReview;

/// <summary>O id e o motivo (obrigatório, até 1.000 caracteres, sem caractere de controle).</summary>
public sealed class HideReviewCommandValidator : AbstractValidator<HideReviewCommand>
{
    public HideReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithError(ReviewsErrors.ReviewIdRequired);

        RuleFor(x => x.Reason)
            .Must(reason => Review.NormalizeText(reason) is not null).WithError(ReviewsErrors.HideReasonRequired)
            .MaximumLength(Review.CommentMaxLength).WithError(ReviewsErrors.HideReasonTooLong)
            .Must(reason => reason is null || !Review.HasForbiddenControlCharacters(reason))
            .WithError(ReviewsErrors.HideReasonInvalidCharacters);
    }
}
