using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Quantos pets o cantinho já tem em cada noite: a soma das reservas que ocupam vaga
/// (Confirmada ou Em andamento). "Capacidade honesta" (escopo, regra 2): em nenhuma noite do
/// período a soma, mais os pets do novo pedido, pode passar da capacidade.
/// </summary>
public sealed class ListingOccupancy
{
    private readonly IReadOnlyList<(StayPeriod Period, int PetCount)> _stays;

    private ListingOccupancy(IReadOnlyList<(StayPeriod Period, int PetCount)> stays) => _stays = stays;

    /// <summary>Cantinho vazio.</summary>
    public static ListingOccupancy Empty { get; } = new([]);

    /// <summary>A ocupação a partir das reservas do cantinho. Só as que ocupam vaga contam.</summary>
    public static ListingOccupancy Of(IEnumerable<Booking> bookings)
    {
        ArgumentNullException.ThrowIfNull(bookings);

        return new([.. bookings.Where(b => b.OccupiesCapacity).Select(b => (b.Period, b.PetCount))]);
    }

    /// <summary>Quantos pets já estão no cantinho na noite que começa em <paramref name="night"/>.</summary>
    public int PetsOn(DateOnly night) => _stays.Where(stay => stay.Period.Covers(night)).Sum(stay => stay.PetCount);

    /// <summary>
    /// Cabem mais <paramref name="petCount"/> pets em todas as noites de <paramref name="period"/>?
    /// Senão, <c>BOOKING_CAPACITY_EXCEEDED</c> com a primeira noite cheia.
    /// </summary>
    public Result EnsureFits(StayPeriod period, int petCount, int capacity)
    {
        ArgumentNullException.ThrowIfNull(period);

        foreach (var night in period.EachNight())
        {
            if (PetsOn(night) + petCount > capacity)
                return Result.Failure(BookingsErrors.CapacityExceeded(night));
        }

        return Result.Success();
    }
}
