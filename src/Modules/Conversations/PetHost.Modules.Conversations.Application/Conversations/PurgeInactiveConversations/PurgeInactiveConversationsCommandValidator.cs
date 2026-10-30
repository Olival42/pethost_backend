using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;

/// <summary>
/// A configuração da tarefa: prazo de pelo menos um dia e lote de 1 a
/// <see cref="ConversationsErrors.MaxRetentionBatchSize"/>. Prazo zero apagaria tudo.
/// </summary>
public sealed class PurgeInactiveConversationsCommandValidator : AbstractValidator<PurgeInactiveConversationsCommand>
{
    public PurgeInactiveConversationsCommandValidator()
    {
        RuleFor(x => x.InactivityDays)
            .GreaterThan(0).WithError(ConversationsErrors.InactivityDaysInvalid);

        RuleFor(x => x.BatchSize)
            .InclusiveBetween(1, ConversationsErrors.MaxRetentionBatchSize)
            .WithError(ConversationsErrors.BatchSizeInvalid);
    }
}
