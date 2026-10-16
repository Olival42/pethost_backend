namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>Corpo de <c>POST /api/v1/conversations/{conversationId}/messages</c>.</summary>
/// <param name="Body">O texto, até 2.000 caracteres.</param>
public sealed record SendMessageRequest(string? Body);
