using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;

/// <summary>
/// Denúncia, desfecho (<c>upheld</c> ou <c>dismissed</c>) e observação obrigatória: até
/// <see cref="MessageReport.ResolutionNoteMaxLength"/> caracteres, sem caractere de controle.
/// </summary>
public sealed class ResolveMessageReportCommandValidator : AbstractValidator<ResolveMessageReportCommand>
{
    public ResolveMessageReportCommandValidator()
    {
        RuleFor(x => x.ReportId)
            .NotEmpty().WithError(ConversationsErrors.ReportIdRequired);

        RuleFor(x => x.Outcome)
            .Must(outcome => ReportStatusValues.TryOutcomeFromWire(outcome, out _)).WithError(ConversationsErrors.OutcomeInvalid);

        RuleFor(x => x.Note)
            .Cascade(CascadeMode.Stop)
            .Must(note => !string.IsNullOrWhiteSpace(note)).WithError(ConversationsErrors.NoteRequired)
            .Must(note => note!.Trim().Length <= MessageReport.ResolutionNoteMaxLength).WithError(ConversationsErrors.NoteTooLong)
            .Must(note => !Message.HasControlCharacters(note!)).WithError(ConversationsErrors.NoteInvalidCharacters);
    }
}
