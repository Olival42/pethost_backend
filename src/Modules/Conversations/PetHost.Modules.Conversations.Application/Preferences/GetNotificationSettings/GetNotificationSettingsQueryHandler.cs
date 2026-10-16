using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Preferences.GetNotificationSettings;

/// <summary>A escolha da conta; quem nunca mudou recebe tudo.</summary>
public sealed class GetNotificationSettingsQueryHandler(INotificationPreferenceRepository preferenceRepository)
    : IQueryHandler<GetNotificationSettingsQuery, NotificationSettingsResponse>
{
    public async Task<Result<NotificationSettingsResponse>> HandleAsync(GetNotificationSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var preference = await preferenceRepository.GetAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        return Result<NotificationSettingsResponse>.Success(new NotificationSettingsResponse(preference?.UnreadMessagesEmail ?? true));
    }
}
