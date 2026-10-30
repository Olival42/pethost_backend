namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Valores de <see cref="BookingStatus"/> no banco e no JSON (<c>snake_case</c>).</summary>
public static class BookingStatusValues
{
    private static readonly Dictionary<BookingStatus, string> Wire = new()
    {
        [BookingStatus.Requested] = "requested",
        [BookingStatus.Accepted] = "accepted",
        [BookingStatus.Confirmed] = "confirmed",
        [BookingStatus.InProgress] = "in_progress",
        [BookingStatus.Completed] = "completed",
        [BookingStatus.Declined] = "declined",
        [BookingStatus.Expired] = "expired",
        [BookingStatus.Cancelled] = "cancelled",
    };

    private static readonly Dictionary<string, BookingStatus> FromWireValue =
        Wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Todos os valores aceitos, na ordem do ciclo.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Wire.Values];

    public static string ToWire(BookingStatus status) => Wire[status];

    /// <summary>Aceita maiúsculas/minúsculas e espaços nas pontas.</summary>
    public static bool TryParse(string? value, out BookingStatus status) =>
        FromWireValue.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out status);
}
