namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Valores de <see cref="Weekdays"/> no JSON, de segunda a domingo.</summary>
public static class WeekdaysValues
{
    private static readonly (Weekdays Day, string Wire)[] Wire =
    [
        (Weekdays.Monday, "monday"),
        (Weekdays.Tuesday, "tuesday"),
        (Weekdays.Wednesday, "wednesday"),
        (Weekdays.Thursday, "thursday"),
        (Weekdays.Friday, "friday"),
        (Weekdays.Saturday, "saturday"),
        (Weekdays.Sunday, "sunday"),
    ];

    /// <summary>Todos os valores aceitos, de segunda a domingo.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Wire.Select(pair => pair.Wire)];

    /// <summary>A semana inteira: o maior valor que a coluna aceita.</summary>
    public static Weekdays Every { get; } = Wire.Aggregate(Weekdays.None, (all, pair) => all | pair.Day);

    /// <summary>O conjunto como lista, de segunda a domingo.</summary>
    public static IReadOnlyList<string> ToWire(Weekdays days) =>
        [.. Wire.Where(pair => days.HasFlag(pair.Day)).Select(pair => pair.Wire)];

    /// <summary>O dia está no conjunto?</summary>
    public static bool Includes(Weekdays days, DayOfWeek day) => days.HasFlag((Weekdays)(1 << (int)day));

    /// <summary>
    /// Junta os valores num conjunto. Aceita maiúsculas/minúsculas, espaços nas pontas e
    /// repetição. Falso se algum valor não é conhecido.
    /// </summary>
    public static bool TryParse(IEnumerable<string?> values, out Weekdays days)
    {
        ArgumentNullException.ThrowIfNull(values);

        days = Weekdays.None;

        foreach (var value in values)
        {
            var text = value?.Trim().ToLowerInvariant();
            var match = Array.Find(Wire, pair => pair.Wire == text);

            if (match.Wire is null)
                return false;

            days |= match.Day;
        }

        return true;
    }
}
