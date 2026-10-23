using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;

/// <summary>A configuração da tarefa: espera e intervalo de pelo menos um minuto, lote de 1 a 1000.</summary>
public sealed class NotifyUnreadMessagesCommandValidator : AbstractValidator<NotifyUnreadMessagesCommand>
{
    public NotifyUnreadMessagesCommandValidator()
    {
        RuleFor(x => x.DelayMinutes)
            .GreaterThan(0).WithError(ConversationsErrors.UnreadDelayInvalid);

        RuleFor(x => x.CooldownMinutes)
            .GreaterThan(0).WithError(ConversationsErrors.UnreadCooldownInvalid);

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, ConversationsErrors.MaxRetentionBatchSize)
            .WithError(ConversationsErrors.BatchSizeInvalid);
    }
}
