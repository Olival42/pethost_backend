namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>
/// Valores de <see cref="PersonType"/> no banco e no JSON: <c>pf</c> (pessoa física) e
/// <c>pj</c> (pessoa jurídica). Na entrada, maiúsculas também valem (<c>PF</c>, <c>PJ</c>).
/// </summary>
public static class PersonTypeValues
{
    public const string Individual = "pf";
    public const string Company = "pj";

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
