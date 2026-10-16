namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>Corpo de <c>POST /api/v1/conversations</c>.</summary>
/// <param name="ListingId">O cantinho onde o tutor tocou em "Puxar conversa".</param>
public sealed record StartConversationRequest(Guid ListingId);
