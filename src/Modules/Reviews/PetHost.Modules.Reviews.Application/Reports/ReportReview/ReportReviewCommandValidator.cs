using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reports.ReportReview;

/// <summary>Formato da denúncia. Quem pode denunciar é conferido pelo handler e pelo agregado.</summary>
public sealed class ReportReviewCommandValidator : AbstractValidator<ReportReviewCommand>
{
    public ReportReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage(ReviewsErrors.ReviewIdRequired.Message);

        RuleFor(x => x.Reason)
            .Must(reason => ReviewReportReasonValues.TryFromWire(reason, out _)).WithMessage(ReviewsErrors.ReasonInvalid.Message);

        RuleFor(x => x.Description)
            .Must(description => Review.NormalizeText(description) is not null).WithMessage(ReviewsErrors.DescriptionRequired.Message)
            .When(x => x.Reason == ReviewReportReasonValues.Other);

        RuleFor(x => x.Description)
            .MaximumLength(ReviewReport.TextMaxLength).WithMessage(ReviewsErrors.DescriptionTooLong.Message)
            .Must(description => description is null || !Review.HasForbiddenControlCharacters(description))
            .WithMessage(ReviewsErrors.DescriptionInvalidCharacters.Message);
    }
}
