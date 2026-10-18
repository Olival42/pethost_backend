using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;

namespace PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;

/// <summary>O lote tem teto.</summary>
public sealed class NotifyPublishedReviewsCommandValidator : AbstractValidator<NotifyPublishedReviewsCommand>
{
    public NotifyPublishedReviewsCommandValidator()
    {
        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, ReviewsErrors.MaxBatchSize).WithMessage(ReviewsErrors.BatchSizeInvalid.Message);
    }
}
