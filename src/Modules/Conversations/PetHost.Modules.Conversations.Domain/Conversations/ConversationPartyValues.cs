namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>Valores de <see cref="ConversationParty"/> no banco e no JSON.</summary>
public static class ConversationPartyValues
{
    public const string Owner = "owner";
    public const string Host = "host";

    public static IReadOnlyList<string> All { get; } = [Owner, Host];

    public static string ToWire(ConversationParty party) => party == ConversationParty.Owner ? Owner : Host;

    public static ConversationParty FromWire(string value) => value switch
    {
        Owner => ConversationParty.Owner,
        Host => ConversationParty.Host,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown conversation party."),
    };

    /// <summary>O outro lado: quem recebe o que <paramref name="party"/> envia.</summary>
    public static ConversationParty Other(ConversationParty party) =>
        party == ConversationParty.Owner ? ConversationParty.Host : ConversationParty.Owner;
}
