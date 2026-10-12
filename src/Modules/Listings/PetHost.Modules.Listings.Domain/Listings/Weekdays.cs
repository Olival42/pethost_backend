namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Dias da semana em que o cantinho recebe pets, como conjunto de bits (coluna
/// <c>available_weekdays</c>). O anfitrião escolhe; a agenda bloqueia datas por cima disso.
/// </summary>
/// <remarks>Os bits seguem <see cref="DayOfWeek"/>: <c>1 &lt;&lt; (int)DayOfWeek</c>.</remarks>
[Flags]
public enum Weekdays
{
    None = 0,
    Sunday = 1 << (int)DayOfWeek.Sunday,
    Monday = 1 << (int)DayOfWeek.Monday,
    Tuesday = 1 << (int)DayOfWeek.Tuesday,
    Wednesday = 1 << (int)DayOfWeek.Wednesday,
    Thursday = 1 << (int)DayOfWeek.Thursday,
    Friday = 1 << (int)DayOfWeek.Friday,
    Saturday = 1 << (int)DayOfWeek.Saturday,
}
