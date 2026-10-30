namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>Corpo de <c>PUT /api/v1/conversations/me/notification-settings</c>.</summary>
/// <param name="UnreadMessagesEmail"><c>true</c> liga, <c>false</c> desliga o e-mail de mensagens não lidas.</param>
public sealed record UpdateNotificationSettingsRequest(bool? UnreadMessagesEmail);
