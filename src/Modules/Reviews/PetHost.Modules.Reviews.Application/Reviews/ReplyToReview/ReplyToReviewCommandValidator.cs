using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;

/// <summary>Formato da resposta. Quem pode responder é conferido pelo agregado.</summary>
public sealed class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithError(ReviewsErrors.ReviewIdRequired);

        RuleFor(x => x.Text)
            .Must(text => Review.NormalizeText(text) is not null).WithError(ReviewsErrors.ReplyRequired)
            .MaximumLength(Review.CommentMaxLength).WithError(ReviewsErrors.ReplyTooLong)
            .Must(text => text is null || !Review.HasForbiddenControlCharacters(text))
            .WithError(ReviewsErrors.ReplyInvalidCharacters);
    }
}
