namespace PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;

/// <summary>Quantas conversas foram apagadas nesta rodada.</summary>
public sealed record PurgeInactiveConversationsResponse(int Purged);
