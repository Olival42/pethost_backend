using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>
/// Um período em que o anfitrião fechou a agenda do cantinho (tabela
/// <c>listing.availability_blocks</c>). Por padrão tudo está disponível; o bloqueio fecha os dias de
/// <see cref="StartDate"/> a <see cref="EndDate"/>, inclusive.
/// </summary>
/// <remarks>
/// Vale como os dias da semana: o pedido de reserva precisa de todos os dias da estadia, da
/// chegada à saída, livres. Reservas já pedidas ou aceitas não mudam — o anfitrião recusa ou
/// cancela, se for o caso. O motivo é só para o anfitrião.
/// </remarks>
public sealed class AvailabilityBlock : Entity<AvailabilityBlockId>
{
    /// <summary>Tamanho máximo do motivo.</summary>
    public const int ReasonMaxLength = 120;

    /// <summary>Quantos bloqueios em aberto (que ainda não acabaram) um cantinho pode ter.</summary>
    public const int MaxOpenBlocks = 50;

    /// <summary>Até quantos dias à frente dá para bloquear (o mesmo horizonte do pedido de reserva).</summary>
    public const int MaxDaysAhead = StayWindow.MaxDaysAhead;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private AvailabilityBlock()
    {
    }

    private AvailabilityBlock(ListingId listingId, DateOnly startDate, DateOnly endDate, string? reason, DateTimeOffset now)
        : base(AvailabilityBlockId.New())
    {
        ListingId = listingId;
        StartDate = startDate;
        EndDate = endDate;
        Reason = reason;
        CreatedAt = now;
    }

    public ListingId ListingId { get; private set; }

    /// <summary>Primeiro dia bloqueado.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Último dia bloqueado (inclusive).</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Motivo opcional, só para o anfitrião.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Quantos dias o bloqueio fecha.</summary>
    public int Days => EndDate.DayNumber - StartDate.DayNumber + 1;

    /// <summary>
    /// Fecha a agenda do cantinho de <paramref name="startDate"/> a <paramref name="endDate"/>.
    /// Confere, juntando os erros de campo: início a partir de hoje (Brasília), fim não antes do
    /// início e até <see cref="MaxDaysAhead"/> dias à frente, motivo até
    /// <see cref="ReasonMaxLength"/> caracteres. Depois: até <see cref="MaxOpenBlocks"/> bloqueios
    /// em aberto e sem sobrepor outro bloqueio do cantinho.
    /// </summary>
    /// <param name="openBlocks">Os bloqueios do cantinho que ainda não acabaram.</param>
    public static Result<AvailabilityBlock> Create(
        ListingId listingId,
        DateOnly startDate,
        DateOnly endDate,
        string? reason,
        IReadOnlyCollection<AvailabilityBlock> openBlocks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(openBlocks);

        var period = CheckPeriod(startDate, endDate, StayWindow.Today(now));
        var errors = period.IsFailure ? new List<Error>(period.Errors!) : [];

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text?.Length > ReasonMaxLength)
            errors.Add(ListingsErrors.AvailabilityBlockReasonTooLong);

        if (errors.Count > 0)
            return Result<AvailabilityBlock>.Failure(errors);

        if (openBlocks.Count >= MaxOpenBlocks)
            return Result<AvailabilityBlock>.Failure(ListingsErrors.AvailabilityBlockLimitReached);

        if (openBlocks.FirstOrDefault(block => block.Overlaps(startDate, endDate)) is { } existing)
            return Result<AvailabilityBlock>.Failure(ListingsErrors.AvailabilityBlockOverlap(existing.StartDate, existing.EndDate));

        return Result<AvailabilityBlock>.Success(new AvailabilityBlock(listingId, startDate, endDate, text, now));
    }

    /// <summary>
    /// As regras das datas, com os erros nos campos <c>startDate</c> e <c>endDate</c>. Usada também
    /// pelo validador, para os erros voltarem juntos com os de formato.
    /// </summary>
    public static Result CheckPeriod(DateOnly startDate, DateOnly endDate, DateOnly today)
    {
        var errors = new List<Error>();

        if (startDate < today)
            errors.Add(ListingsErrors.AvailabilityBlockStartInThePast);
        else if (startDate > today.AddDays(MaxDaysAhead))
            errors.Add(ListingsErrors.AvailabilityBlockStartTooFarAhead);

        if (endDate < startDate)
            errors.Add(ListingsErrors.AvailabilityBlockEndBeforeStart);
        else if (endDate > today.AddDays(MaxDaysAhead))
            errors.Add(ListingsErrors.AvailabilityBlockEndTooFarAhead);

        return errors.Count > 0 ? Result.Failure(errors) : Result.Success();
    }

    /// <summary>O bloqueio fecha algum dia entre <paramref name="from"/> e <paramref name="to"/> (inclusive)?</summary>
    public bool Overlaps(DateOnly from, DateOnly to) => StartDate <= to && EndDate >= from;

    /// <summary>O bloqueio já acabou em <paramref name="today"/>?</summary>
    public bool HasEnded(DateOnly today) => EndDate < today;
}
