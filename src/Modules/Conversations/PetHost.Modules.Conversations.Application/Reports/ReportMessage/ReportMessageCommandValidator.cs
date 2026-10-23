using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Reports.ReportMessage;

/// <summary>
/// Conversa e mensagem, motivo da lista e descrição: até <see cref="MessageReport.DescriptionMaxLength"/>
/// caracteres sem os espaços nas pontas, sem caractere de controle, obrigatória no motivo <c>other</c>.
/// </summary>
public sealed class ReportMessageCommandValidator : AbstractValidator<ReportMessageCommand>
{
    public ReportMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithError(ConversationsErrors.ConversationIdRequired);

        RuleFor(x => x.MessageId)
            .NotEmpty().WithError(ConversationsErrors.MessageIdRequired);

        RuleFor(x => x.Reason)
            .Must(reason => ReportReasonValues.TryFromWire(reason, out _)).WithError(ConversationsErrors.ReasonInvalid);

        RuleFor(x => x.Description)
            .Must(description => !string.IsNullOrWhiteSpace(description))
            .When(x => x.Reason == ReportReasonValues.Other)
            .WithError(ConversationsErrors.DescriptionRequired);

        RuleFor(x => x.Description)
            .Cascade(CascadeMode.Stop)
            .Must(description => description!.Trim().Length <= MessageReport.DescriptionMaxLength).WithError(ConversationsErrors.DescriptionTooLong)
            .Must(description => !Message.HasControlCharacters(description!)).WithError(ConversationsErrors.DescriptionInvalidCharacters)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}
