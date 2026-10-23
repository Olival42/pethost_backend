using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Conversations.BlockConversation;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class BlockConversationCommandValidator : AbstractValidator<BlockConversationCommand>
{
    public BlockConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithError(ConversationsErrors.ConversationIdRequired);
    }
}
