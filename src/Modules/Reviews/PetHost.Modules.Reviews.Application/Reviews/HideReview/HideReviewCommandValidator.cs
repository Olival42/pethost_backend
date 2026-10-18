using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews.HideReview;

/// <summary>O id e o motivo (obrigatório, até 1.000 caracteres, sem caractere de controle).</summary>
public sealed class HideReviewCommandValidator : AbstractValidator<HideReviewCommand>
{
    public HideReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);

        RuleFor(x => x.Reason)
            .Must(reason => Review.NormalizeText(reason) is not null).WithMessage(ReviewsErrors.HideReasonRequired.Message)
            .MaximumLength(Review.CommentMaxLength).WithMessage(ReviewsErrors.HideReasonTooLong.Message)
            .Must(reason => reason is null || !Review.HasForbiddenControlCharacters(reason))
            .WithMessage(ReviewsErrors.HideReasonInvalidCharacters.Message);
    }
}
