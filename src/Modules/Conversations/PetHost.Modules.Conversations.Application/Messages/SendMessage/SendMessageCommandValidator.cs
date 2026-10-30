using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Messages.SendMessage;

/// <summary>
/// Conversa e texto: obrigatório, até <see cref="Message.BodyMaxLength"/> caracteres sem os espaços
/// nas pontas e sem caractere de controle (<see cref="Message.HasControlCharacters"/>).
/// </summary>
public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithError(ConversationsErrors.ConversationIdRequired);

        RuleFor(x => x.Body)
            .Cascade(CascadeMode.Stop)
            .Must(body => !string.IsNullOrWhiteSpace(body)).WithError(ConversationsErrors.BodyRequired)
            .Must(body => body!.Trim().Length <= Message.BodyMaxLength).WithError(ConversationsErrors.BodyTooLong)
            .Must(body => !Message.HasControlCharacters(body!)).WithError(ConversationsErrors.BodyInvalidCharacters);
    }
}
