namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Valores de <see cref="BookingParty"/> no banco e no JSON.</summary>
public static class BookingPartyValues
{
    public const string Owner = "owner";
    public const string Host = "host";

    public static IReadOnlyList<string> All { get; } = [Owner, Host];

    public static string ToWire(BookingParty party) => party == BookingParty.Owner ? Owner : Host;

    public static BookingParty FromWire(string value) => value switch
    {
        Owner => BookingParty.Owner,
        Host => BookingParty.Host,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown booking party."),
    };
}
