using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;

namespace PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class UnblockConversationCommandValidator : AbstractValidator<UnblockConversationCommand>
{
    public UnblockConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithMessage(ConversationsErrors.ConversationIdRequired.Message);
    }
}
