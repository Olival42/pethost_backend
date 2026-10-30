namespace PetHost.Modules.Conversations.Domain.Messages;

/// <summary>Identidade de <see cref="Message"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
