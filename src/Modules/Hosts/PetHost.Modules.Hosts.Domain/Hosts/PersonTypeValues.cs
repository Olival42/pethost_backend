namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>Valores de <see cref="PersonType"/> no banco e no JSON.</summary>
public static class PersonTypeValues
{
    public const string Individual = "individual";
    public const string Company = "company";

    public static string ToWire(PersonType type) => type switch
    {
        PersonType.Individual => Individual,
        PersonType.Company => Company,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown person type."),
    };

    public static PersonType FromWire(string value) => value switch
    {
        Individual => PersonType.Individual,
        Company => PersonType.Company,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown person type."),
    };
}
