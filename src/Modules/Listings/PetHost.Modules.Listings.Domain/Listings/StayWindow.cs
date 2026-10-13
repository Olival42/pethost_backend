using System.Globalization;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// As datas da busca ("de 20 a 25 de dezembro"): só aparecem cantinhos que recebem em todos os
/// dias e têm vaga em todas as noites. Mesmas regras do pedido de reserva: chegada a partir de
/// hoje (horário de Brasília) e até 365 dias à frente, saída depois da chegada, até 30 noites.
/// </summary>
public sealed record StayWindow(DateOnly CheckIn, DateOnly CheckOut)
{
    /// <summary>Formato das datas na query string.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    public const int MaxNights = 30;
    public const int MaxDaysAhead = 365;

    /// <summary>UTC−3 (Brasília, sem horário de verão): o "hoje" das datas de calendário.</summary>
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(-3);

    /// <summary>O dia de calendário em Brasília no instante <paramref name="now"/>.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(LocalOffset).DateTime);

    /// <summary>
    /// Lê e confere as datas. As duas vêm juntas ou nenhuma (<c>null</c>, sem filtro de data).
    /// Erros de validação acumulados nos campos <c>checkIn</c> e <c>checkOut</c>.
    /// </summary>
    public static Result<StayWindow?> Parse(string? checkIn, string? checkOut, DateOnly today)
    {
        var hasCheckIn = !string.IsNullOrWhiteSpace(checkIn);
        var hasCheckOut = !string.IsNullOrWhiteSpace(checkOut);

        if (!hasCheckIn && !hasCheckOut)
            return Result<StayWindow?>.Success(null);

        if (hasCheckIn != hasCheckOut)
            return Result<StayWindow?>.Failure(hasCheckIn ? ListingsErrors.CheckOutFilterRequired : ListingsErrors.CheckInFilterRequired);

        var errors = new List<Error>();

        var inParsed = TryParse(checkIn, out var from);
        var outParsed = TryParse(checkOut, out var to);

        if (!inParsed)
            errors.Add(ListingsErrors.CheckInFilterInvalid);
        else if (from < today)
            errors.Add(ListingsErrors.CheckInFilterInThePast);
        else if (from > today.AddDays(MaxDaysAhead))
            errors.Add(ListingsErrors.CheckInFilterTooFarAhead);

        if (!outParsed)
            errors.Add(ListingsErrors.CheckOutFilterInvalid);
        else if (inParsed && to <= from)
            errors.Add(ListingsErrors.CheckOutFilterNotAfterCheckIn);
        else if (inParsed && to.DayNumber - from.DayNumber > MaxNights)
            errors.Add(ListingsErrors.StayFilterTooLong);

        return errors.Count > 0
            ? Result<StayWindow?>.Failure(errors)
            : Result<StayWindow?>.Success(new StayWindow(from, to));
    }

    /// <summary>
    /// Os dias da semana do período, da chegada à saída (inclusive): o cantinho precisa receber
    /// em todos — a mesma regra do pedido de reserva.
    /// </summary>
    public Weekdays RequiredWeekdays() =>
        Enumerable.Range(0, CheckOut.DayNumber - CheckIn.DayNumber + 1)
            .Select(offset => CheckIn.AddDays(offset).DayOfWeek)
            .Aggregate(Weekdays.None, (days, day) => days | (Weekdays)(1 << (int)day));

    private static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
