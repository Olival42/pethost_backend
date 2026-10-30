namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>Um dos dois lados da conversa: o tutor ou o anfitrião.</summary>
public enum ConversationParty
{
    /// <summary>O tutor, que puxou a conversa.</summary>
    Owner,

    /// <summary>O anfitrião do cantinho.</summary>
    Host,
}
