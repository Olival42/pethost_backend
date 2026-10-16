using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;

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
            .NotEmpty().WithMessage(ConversationsErrors.ReportIdRequired.Message);

        RuleFor(x => x.Outcome)
            .Must(outcome => ReportStatusValues.TryOutcomeFromWire(outcome, out _)).WithMessage(ConversationsErrors.OutcomeInvalid.Message);

        RuleFor(x => x.Note)
            .Cascade(CascadeMode.Stop)
            .Must(note => !string.IsNullOrWhiteSpace(note)).WithMessage(ConversationsErrors.NoteRequired.Message)
            .Must(note => note!.Trim().Length <= MessageReport.ResolutionNoteMaxLength).WithMessage(ConversationsErrors.NoteTooLong.Message)
            .Must(note => !Message.HasControlCharacters(note!)).WithMessage(ConversationsErrors.NoteInvalidCharacters.Message);
    }
}
