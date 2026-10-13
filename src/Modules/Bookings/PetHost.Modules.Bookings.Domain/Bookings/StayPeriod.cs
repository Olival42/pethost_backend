using System.Globalization;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// O período da hospedagem: dia de chegada (<see cref="CheckIn"/>) e dia de saída
/// (<see cref="CheckOut"/>), datas de calendário. Noites = saída − chegada (escopo, regra 4).
/// </summary>
/// <remarks>
/// A ocupação é por <b>noite</b>: a noite de um dia vai dele até o dia seguinte. Quem sai no
/// dia 25 e quem chega no dia 25 não dividem noite — a vaga do primeiro passa para o segundo.
/// </remarks>
public sealed class StayPeriod : ValueObject
{
    /// <summary>Estadia mais longa que se pode pedir.</summary>
    public const int MaxNights = 30;

    /// <summary>Com quantos dias de antecedência, no máximo, se pode pedir.</summary>
    public const int MaxDaysAhead = 365;

    /// <summary>Formato das datas no JSON (ISO 8601, só a data).</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Lê uma data no formato <see cref="DateFormat"/>. Falso se vazio ou fora do formato.</summary>
    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Construtor só para o EF Core materializar o tipo complexo.</summary>
    private StayPeriod()
    {
    }

    private StayPeriod(DateOnly checkIn, DateOnly checkOut)
    {
        CheckIn = checkIn;
        CheckOut = checkOut;
    }

    /// <summary>Dia em que o pet chega na casa do anfitrião.</summary>
    public DateOnly CheckIn { get; private set; }

    /// <summary>Dia em que o pet volta pra casa. Sempre depois de <see cref="CheckIn"/>.</summary>
    public DateOnly CheckOut { get; private set; }

    /// <summary>Quantas noites o pet passa fora.</summary>
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    /// <summary>Cada noite, pelo dia em que começa: da chegada até a véspera da saída.</summary>
    public IEnumerable<DateOnly> EachNight() => Enumerable.Range(0, Nights).Select(CheckIn.AddDays);

    /// <summary>Cada dia em que o pet está com o anfitrião, da chegada à saída (inclusive).</summary>
    public IEnumerable<DateOnly> EachDay() => Enumerable.Range(0, Nights + 1).Select(CheckIn.AddDays);

    /// <summary>A noite que começa em <paramref name="night"/> é deste período?</summary>
    public bool Covers(DateOnly night) => night >= CheckIn && night < CheckOut;

    /// <summary>Os dois períodos dividem ao menos uma noite?</summary>
    public bool Overlaps(StayPeriod other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return CheckIn < other.CheckOut && other.CheckIn < CheckOut;
    }

    /// <summary>
    /// Confere o período pedido em <paramref name="today"/>: chegada a partir de hoje e no máximo
    /// <see cref="MaxDaysAhead"/> dias à frente; saída depois da chegada; até
    /// <see cref="MaxNights"/> noites. Devolve todos os erros juntos (<c>VALIDATION_ERROR</c>).
    /// </summary>
    public static Result<StayPeriod> Create(DateOnly checkIn, DateOnly checkOut, DateOnly today)
    {
        var errors = new List<Error>();

        if (checkIn < today)
            errors.Add(BookingsErrors.CheckInInThePast);
        else if (checkIn > today.AddDays(MaxDaysAhead))
            errors.Add(BookingsErrors.CheckInTooFarAhead);

        if (checkOut <= checkIn)
            errors.Add(BookingsErrors.CheckOutNotAfterCheckIn);
        else if (checkOut.DayNumber - checkIn.DayNumber > MaxNights)
            errors.Add(BookingsErrors.StayTooLong);

        if (errors.Count > 0)
            return Result<StayPeriod>.Failure(errors);

        return Result<StayPeriod>.Success(new StayPeriod(checkIn, checkOut));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CheckIn;
        yield return CheckOut;
    }
}
