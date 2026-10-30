namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>Um dos dois lados da estadia: quem escreve a avaliação ou quem é avaliado.</summary>
public enum ReviewParty
{
    /// <summary>O tutor: avalia o anfitrião e o cantinho.</summary>
    Owner,

    /// <summary>O anfitrião: avalia o tutor e os pets.</summary>
    Host,
}

/// <summary>Valores de <see cref="ReviewParty"/> no banco e no JSON.</summary>
public static class ReviewPartyValues
{
    public const string Owner = "owner";
    public const string Host = "host";

    public static IReadOnlyList<string> All { get; } = [Owner, Host];

    public static string ToWire(ReviewParty party) => party == ReviewParty.Owner ? Owner : Host;

    public static ReviewParty FromWire(string value) => value switch
    {
        Owner => ReviewParty.Owner,
        Host => ReviewParty.Host,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown review party."),
    };

    /// <summary>O outro lado: quem é avaliado quando <paramref name="party"/> escreve.</summary>
    public static ReviewParty Other(ReviewParty party) =>
        party == ReviewParty.Owner ? ReviewParty.Host : ReviewParty.Owner;
}
