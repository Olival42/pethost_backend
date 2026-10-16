using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;

/// <summary>
/// Liga ou desliga o e-mail de mensagens não lidas da conta. A primeira mudança cria a linha; repetir
/// a mesma escolha não grava nada. Vale para a próxima rodada do aviso.
/// </summary>
public sealed class UpdateNotificationSettingsCommandHandler(
    INotificationPreferenceRepository preferenceRepository,
    IConversationsUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<UpdateNotificationSettingsCommand, NotificationSettingsResponse>
{
    public async Task<Result<NotificationSettingsResponse>> HandleAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UnreadMessagesEmail is not { } enabled)
            return Result<NotificationSettingsResponse>.Failure(ConversationsErrors.UnreadMessagesEmailRequired);

        var now = timeProvider.GetUtcNow();
        var preference = await preferenceRepository.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);

        bool changed;
        if (preference is null)
        {
            preferenceRepository.Add(NotificationPreference.Create(command.UserId, enabled, now));
            changed = true;
        }
        else
        {
            changed = preference.SetUnreadMessagesEmail(enabled, now);
        }

        if (changed)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<NotificationSettingsResponse>.FromFailure(saved);
        }

        return Result<NotificationSettingsResponse>.Success(new NotificationSettingsResponse(enabled));
    }
}
