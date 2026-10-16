using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;

namespace PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;

/// <summary>A configuração da tarefa: espera e intervalo de pelo menos um minuto, lote de 1 a 1000.</summary>
public sealed class NotifyUnreadMessagesCommandValidator : AbstractValidator<NotifyUnreadMessagesCommand>
{
    public NotifyUnreadMessagesCommandValidator()
    {
        RuleFor(x => x.DelayMinutes)
            .GreaterThan(0).WithMessage(ConversationsErrors.UnreadDelayInvalid.Message);

        RuleFor(x => x.CooldownMinutes)
            .GreaterThan(0).WithMessage(ConversationsErrors.UnreadCooldownInvalid.Message);

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, ConversationsErrors.MaxRetentionBatchSize)
            .WithMessage(ConversationsErrors.BatchSizeInvalid.Message);
    }
}
