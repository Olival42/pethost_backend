namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// As condições do cantinho no momento do pedido (ou do aceite), lidas do módulo Listings. O
/// domínio da reserva confere contra elas: o que o cantinho aceita, os dias em que recebe e a
/// capacidade.
/// </summary>
/// <param name="Id">Id do cantinho (<c>listing.listings.id</c>).</param>
/// <param name="HostId">Anfitrião dono (<c>host.hosts.id</c>).</param>
/// <param name="IsBookable">
/// Cantinho ativo de anfitrião ativo, sem suspensão e com os repasses liberados pelo Stripe — o
/// mesmo critério da busca.
/// </param>
/// <param name="Capacity">Quantos pets ao mesmo tempo.</param>
/// <param name="NightlyRate">Diária por pet, em reais.</param>
/// <param name="AcceptedSpecies">Valores de <c>pets.species</c> aceitos.</param>
/// <param name="AvailableWeekdays">Dias da semana em que recebe pets.</param>
/// <param name="ClosedPeriods">Períodos que o anfitrião fechou na agenda (e que ainda não acabaram).</param>
public sealed record BookableListing(
    Guid Id,
    Guid HostId,
    bool IsBookable,
    int Capacity,
    decimal NightlyRate,
    IReadOnlyCollection<string> AcceptedSpecies,
    bool AcceptsSmall,
    bool AcceptsMedium,
    bool AcceptsLarge,
    IReadOnlyCollection<DayOfWeek> AvailableWeekdays,
    IReadOnlyCollection<ClosedPeriod>? ClosedPeriods = null)
{
    /// <summary>Porte pequeno (valor de <c>pets.size</c>).</summary>
    public const string Small = "small";

    /// <summary>Porte médio.</summary>
    public const string Medium = "medium";

    /// <summary>Porte grande.</summary>
    public const string Large = "large";

    /// <summary>O cantinho aceita a espécie (valor de <c>pets.species</c>)?</summary>
    public bool AcceptsSpecies(string species) => AcceptedSpecies.Contains(species, StringComparer.Ordinal);

    /// <summary>O cantinho aceita o porte (valor de <c>pets.size</c>)? Pet sem porte não é barrado pelo porte.</summary>
    public bool AcceptsSize(string? size) => size switch
    {
        null => true,
        Small => AcceptsSmall,
        Medium => AcceptsMedium,
        Large => AcceptsLarge,
        _ => false,
    };

    /// <summary>
    /// O primeiro dia do período (da chegada à saída, inclusive) em que o cantinho não recebe
    /// pets, ou <c>null</c> se recebe em todos.
    /// </summary>
    public DateOnly? FirstUnavailableDay(StayPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        foreach (var day in period.EachDay())
        {
            if (!AvailableWeekdays.Contains(day.DayOfWeek))
                return day;
        }

        return null;
    }

    /// <summary>
    /// O primeiro dia do período (da chegada à saída, inclusive) fechado na agenda pelo
    /// anfitrião, ou <c>null</c> se nenhum está. Mesma regra dos dias da semana.
    /// </summary>
    public DateOnly? FirstBlockedDay(StayPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (ClosedPeriods is not { Count: > 0 } closed)
            return null;

        foreach (var day in period.EachDay())
        {
            if (closed.Any(block => block.Contains(day)))
                return day;
        }

        return null;
    }
}

/// <summary>Um período fechado na agenda do cantinho, de <paramref name="StartDate"/> a <paramref name="EndDate"/> (inclusive).</summary>
public sealed record ClosedPeriod(DateOnly StartDate, DateOnly EndDate)
{
    public bool Contains(DateOnly day) => day >= StartDate && day <= EndDate;
}
