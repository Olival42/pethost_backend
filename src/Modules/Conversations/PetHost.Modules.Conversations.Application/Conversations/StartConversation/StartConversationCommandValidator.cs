using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;

namespace PetHost.Modules.Conversations.Application.Conversations.StartConversation;

/// <summary>Só o cantinho. Se ele aparece na busca é conferido pelo handler.</summary>
public sealed class StartConversationCommandValidator : AbstractValidator<StartConversationCommand>
{
    public StartConversationCommandValidator()
    {
        RuleFor(x => x.ListingId)
            .NotEmpty().WithMessage(ConversationsErrors.ListingIdRequired.Message);
    }
}
