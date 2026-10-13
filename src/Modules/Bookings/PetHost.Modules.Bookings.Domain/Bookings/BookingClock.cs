namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Datas da reserva são de calendário (<see cref="DateOnly"/>), no fuso de Brasília: o "hoje"
/// e o início do dia de chegada saem daqui. O piloto é só em Maringá, e o Brasil não tem
/// horário de verão desde 2019 — por isso um deslocamento fixo, sem tabela de fusos.
/// </summary>
public static class BookingClock
{
    /// <summary>UTC−3 (horário de Brasília).</summary>
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-3);

    /// <summary>O dia de calendário em Brasília no instante <paramref name="now"/>.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(LocalOffset).DateTime);

    /// <summary>Meia-noite (horário de Brasília) do dia <paramref name="date"/>.</summary>
    public static DateTimeOffset StartOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), LocalOffset);
}
