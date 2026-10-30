namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>Identidade de <see cref="Conversation"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
