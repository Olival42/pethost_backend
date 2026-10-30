using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>Uma conversa e o lado de quem pede, já conferido.</summary>
public sealed record ParticipantConversation(Conversation Conversation, ConversationParticipant Participant);
