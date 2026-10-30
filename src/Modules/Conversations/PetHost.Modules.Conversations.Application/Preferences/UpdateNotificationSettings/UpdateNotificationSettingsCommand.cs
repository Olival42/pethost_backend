using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Preferences.UpdateNotificationSettings;

/// <summary>A conta logada liga ou desliga o e-mail de "mensagens não lidas".</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="UnreadMessagesEmail">Obrigatório: <c>true</c> liga, <c>false</c> desliga.</param>
public sealed record UpdateNotificationSettingsCommand(Guid UserId, bool? UnreadMessagesEmail) : ICommand<NotificationSettingsResponse>;
