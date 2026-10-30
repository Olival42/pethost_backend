namespace PetHost.Modules.Conversations.Application.Preferences;

/// <summary>O que a conta recebe do chat fora do app.</summary>
/// <param name="UnreadMessagesEmail">E-mail de "mensagens não lidas" (padrão: ligado).</param>
public sealed record NotificationSettingsResponse(bool UnreadMessagesEmail);
