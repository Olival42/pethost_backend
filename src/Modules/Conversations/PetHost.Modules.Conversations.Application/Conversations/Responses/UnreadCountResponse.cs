namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>Total de mensagens não lidas da conta logada, em todas as conversas (o contador do app).</summary>
public sealed record UnreadCountResponse(int Total);
