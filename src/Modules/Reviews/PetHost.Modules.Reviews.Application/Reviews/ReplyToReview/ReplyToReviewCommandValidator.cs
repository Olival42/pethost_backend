using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;

/// <summary>Formato da resposta. Quem pode responder é conferido pelo agregado.</summary>
public sealed class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);

        RuleFor(x => x.Text)
            .Must(text => Review.NormalizeText(text) is not null).WithMessage(ReviewsErrors.ReplyRequired.Message)
            .MaximumLength(Review.CommentMaxLength).WithMessage(ReviewsErrors.ReplyTooLong.Message)
            .Must(text => text is null || !Review.HasForbiddenControlCharacters(text))
            .WithMessage(ReviewsErrors.ReplyInvalidCharacters.Message);
    }
}
