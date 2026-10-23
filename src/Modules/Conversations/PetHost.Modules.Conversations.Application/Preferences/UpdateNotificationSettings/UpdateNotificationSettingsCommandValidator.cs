using FluentValidation;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;

/// <summary>A escolha é obrigatória: corpo sem <c>unreadMessagesEmail</c> não liga nem desliga nada.</summary>
public sealed class UpdateNotificationSettingsCommandValidator : AbstractValidator<UpdateNotificationSettingsCommand>
{
    public UpdateNotificationSettingsCommandValidator()
    {
        RuleFor(x => x.UnreadMessagesEmail)
            .NotNull().WithError(ConversationsErrors.UnreadMessagesEmailRequired);
    }
}
