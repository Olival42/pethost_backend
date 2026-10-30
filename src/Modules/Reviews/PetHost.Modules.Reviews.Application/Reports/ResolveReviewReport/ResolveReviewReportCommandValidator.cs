using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;

/// <summary>Desfecho e observação. Se a denúncia ainda está pendente é conferido pelo agregado.</summary>
public sealed class ResolveReviewReportCommandValidator : AbstractValidator<ResolveReviewReportCommand>
{
    public ResolveReviewReportCommandValidator()
    {
        RuleFor(x => x.ReportId)
            .NotEmpty().WithError(ReviewsErrors.ReportIdRequired);

        RuleFor(x => x.Outcome)
            .Must(outcome => ReviewReportStatusValues.TryOutcomeFromWire(outcome, out _)).WithError(ReviewsErrors.OutcomeInvalid);

        RuleFor(x => x.Note)
            .Must(note => Review.NormalizeText(note) is not null).WithError(ReviewsErrors.NoteRequired)
            .MaximumLength(ReviewReport.TextMaxLength).WithError(ReviewsErrors.NoteTooLong)
            .Must(note => note is null || !Review.HasForbiddenControlCharacters(note))
            .WithError(ReviewsErrors.NoteInvalidCharacters);
    }
}
