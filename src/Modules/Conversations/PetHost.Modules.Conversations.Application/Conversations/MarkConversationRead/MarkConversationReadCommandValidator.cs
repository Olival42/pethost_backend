using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;

namespace PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class MarkConversationReadCommandValidator : AbstractValidator<MarkConversationReadCommand>
{
    public MarkConversationReadCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty().WithMessage(ConversationsErrors.ConversationIdRequired.Message);
    }
}
