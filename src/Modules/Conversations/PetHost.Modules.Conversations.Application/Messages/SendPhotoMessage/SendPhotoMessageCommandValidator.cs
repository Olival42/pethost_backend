using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;

/// <summary>
/// Conversa e legenda (opcional, até <see cref="Message.BodyMaxLength"/> caracteres, sem caractere
/// de controle). O arquivo
/// (faltando, grande, formato) é conferido pelo bucket, depois de saber que a conversa é de quem
/// pede: um estranho não chega a enviar nada.
/// </summary>
public sealed class SendPhotoMessageCommandValidator : AbstractValidator<SendPhotoMessageCommand>
{
    public SendPhotoMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithMessage(ConversationsErrors.ConversationIdRequired.Message);

        RuleFor(x => x.Body)
            .Must(body => body!.Trim().Length <= Message.BodyMaxLength).WithMessage(ConversationsErrors.BodyTooLong.Message)
            .Must(body => !Message.HasControlCharacters(body!)).WithMessage(ConversationsErrors.BodyInvalidCharacters.Message)
            .When(x => x.Body is not null);
    }
}
