using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;

/// <summary>Desfecho e observação. Se a denúncia ainda está pendente é conferido pelo agregado.</summary>
public sealed class ResolveReviewReportCommandValidator : AbstractValidator<ResolveReviewReportCommand>
{
    public ResolveReviewReportCommandValidator()
    {
        RuleFor(x => x.ReportId)
            .NotEmpty().WithMessage(ReviewsErrors.ReportIdRequired.Message);

        RuleFor(x => x.Outcome)
            .Must(outcome => ReviewReportStatusValues.TryOutcomeFromWire(outcome, out _)).WithMessage(ReviewsErrors.OutcomeInvalid.Message);

        RuleFor(x => x.Note)
            .Must(note => Review.NormalizeText(note) is not null).WithMessage(ReviewsErrors.NoteRequired.Message)
            .MaximumLength(ReviewReport.TextMaxLength).WithMessage(ReviewsErrors.NoteTooLong.Message)
            .Must(note => note is null || !Review.HasForbiddenControlCharacters(note))
            .WithMessage(ReviewsErrors.NoteInvalidCharacters.Message);
    }
}
