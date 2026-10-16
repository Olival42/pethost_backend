namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Valores de <see cref="BookingParty"/> no banco e no JSON.</summary>
public static class BookingPartyValues
{
    public const string Owner = "owner";
    public const string Host = "host";
    public const string Platform = "platform";

    public static IReadOnlyList<string> All { get; } = [Owner, Host, Platform];

    public static string ToWire(BookingParty party) => party switch
    {
        BookingParty.Owner => Owner,
        BookingParty.Host => Host,
        BookingParty.Platform => Platform,
        _ => throw new ArgumentOutOfRangeException(nameof(party), party, "Unknown booking party."),
    };

    public static BookingParty FromWire(string value) => value switch
    {
        Owner => BookingParty.Owner,
        Host => BookingParty.Host,
        Platform => BookingParty.Platform,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown booking party."),
    };
}
