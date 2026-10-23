using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reports.ReportReview;

/// <summary>Formato da denúncia. Quem pode denunciar é conferido pelo handler e pelo agregado.</summary>
public sealed class ReportReviewCommandValidator : AbstractValidator<ReportReviewCommand>
{
    public ReportReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithError(ReviewsErrors.ReviewIdRequired);

        RuleFor(x => x.Reason)
            .Must(reason => ReviewReportReasonValues.TryFromWire(reason, out _)).WithError(ReviewsErrors.ReasonInvalid);

        RuleFor(x => x.Description)
            .Must(description => Review.NormalizeText(description) is not null).WithError(ReviewsErrors.DescriptionRequired)
            .When(x => x.Reason == ReviewReportReasonValues.Other);

        RuleFor(x => x.Description)
            .MaximumLength(ReviewReport.TextMaxLength).WithError(ReviewsErrors.DescriptionTooLong)
            .Must(description => description is null || !Review.HasForbiddenControlCharacters(description))
            .WithError(ReviewsErrors.DescriptionInvalidCharacters);
    }
}
