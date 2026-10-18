using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;

namespace PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;

/// <summary>Só o id. Quem pode apagar é conferido pelo agregado.</summary>
public sealed class RemoveReviewReplyCommandValidator : AbstractValidator<RemoveReviewReplyCommand>
{
    public RemoveReviewReplyCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);
    }
}
