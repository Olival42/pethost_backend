using PetHost.Modules.Conversations.Application.Conversations.Responses;

namespace PetHost.Modules.Conversations.Application.Conversations.StartConversation;

/// <summary>A conversa da dupla e se ela nasceu agora (<c>201</c>) ou já existia (<c>200</c>).</summary>
public sealed record StartedConversation(ConversationResponse Conversation, bool Created);
