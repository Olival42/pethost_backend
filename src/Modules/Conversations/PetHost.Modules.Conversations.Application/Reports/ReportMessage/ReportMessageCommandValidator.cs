using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;

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
            .NotEmpty().WithMessage(ConversationsErrors.ConversationIdRequired.Message);

        RuleFor(x => x.MessageId)
            .NotEmpty().WithMessage(ConversationsErrors.MessageIdRequired.Message);

        RuleFor(x => x.Reason)
            .Must(reason => ReportReasonValues.TryFromWire(reason, out _)).WithMessage(ConversationsErrors.ReasonInvalid.Message);

        RuleFor(x => x.Description)
            .Must(description => !string.IsNullOrWhiteSpace(description))
            .When(x => x.Reason == ReportReasonValues.Other)
            .WithMessage(ConversationsErrors.DescriptionRequired.Message);

        RuleFor(x => x.Description)
            .Cascade(CascadeMode.Stop)
            .Must(description => description!.Trim().Length <= MessageReport.DescriptionMaxLength).WithMessage(ConversationsErrors.DescriptionTooLong.Message)
            .Must(description => !Message.HasControlCharacters(description!)).WithMessage(ConversationsErrors.DescriptionInvalidCharacters.Message)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}
