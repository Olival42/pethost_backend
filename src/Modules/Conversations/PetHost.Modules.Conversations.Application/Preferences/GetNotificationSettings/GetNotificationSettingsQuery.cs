using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Preferences.GetNotificationSettings;

/// <summary>O que a conta logada recebe do chat fora do app.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record GetNotificationSettingsQuery(Guid UserId) : IQuery<NotificationSettingsResponse>;
