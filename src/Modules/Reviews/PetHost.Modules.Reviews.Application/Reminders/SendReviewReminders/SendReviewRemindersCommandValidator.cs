using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;

/// <summary>A espera cabe dentro do prazo de avaliar; o lote tem teto.</summary>
public sealed class SendReviewRemindersCommandValidator : AbstractValidator<SendReviewRemindersCommand>
{
    public SendReviewRemindersCommandValidator()
    {
        RuleFor(x => x.DelayHours)
            .InclusiveBetween(1, (Review.WindowDays * 24) - 1).WithMessage(ReviewsErrors.ReminderDelayInvalid.Message);

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, ReviewsErrors.MaxBatchSize).WithMessage(ReviewsErrors.BatchSizeInvalid.Message);
    }
}
