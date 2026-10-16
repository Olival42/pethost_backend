using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;

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
            .NotEmpty().WithMessage(ConversationsErrors.ConversationIdRequired.Message);

        RuleFor(x => x.Body)
            .Cascade(CascadeMode.Stop)
            .Must(body => !string.IsNullOrWhiteSpace(body)).WithMessage(ConversationsErrors.BodyRequired.Message)
            .Must(body => body!.Trim().Length <= Message.BodyMaxLength).WithMessage(ConversationsErrors.BodyTooLong.Message)
            .Must(body => !Message.HasControlCharacters(body!)).WithMessage(ConversationsErrors.BodyInvalidCharacters.Message);
    }
}
