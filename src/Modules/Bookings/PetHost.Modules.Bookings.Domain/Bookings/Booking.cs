using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// A reserva: um tutor, um cantinho, um período (<see cref="Period"/>) e um ou mais pets dele.
/// Guarda o ciclo de status e uma cópia da diária no momento do pedido. Espelha as tabelas
/// <c>booking.bookings</c> e <c>booking.booking_pets</c>.
/// </summary>
/// <remarks>
/// Ciclo (escopo, seção 5): Solicitada → (aceite) Confirmada → (check-in) Em andamento →
/// (check-out) Concluída. Fins antecipados: Recusada, Expirada (24h sem resposta) e Cancelada
/// (tutor ou anfitrião, até o check-in).
/// <para>
/// Enquanto o módulo de pagamento não existe, o aceite do anfitrião leva direto a
/// <see cref="BookingStatus.Confirmed"/>. Com o pagamento, o aceite passa a ir para
/// <see cref="BookingStatus.Accepted"/> e o webhook do Stripe confirma.
/// </para>
/// Guarda ids de outros módulos como FK lógica: tutor (<c>owner.owners.id</c>), cantinho
/// (<c>listing.listings.id</c>), anfitrião (<c>host.hosts.id</c>, copiado do cantinho para a
/// listagem do anfitrião) e pets (<c>pet.pets.id</c>).
/// </remarks>
public sealed class Booking : Entity<BookingId>
{
    /// <summary>Quantos pets cabem num pedido (a capacidade máxima de pessoa física).</summary>
    public const int MaxPets = 10;

    /// <summary>Tamanho máximo do motivo de cancelamento ou de recusa.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>Reembolso inteiro.</summary>
    public const int FullRefundPercentage = 100;

    /// <summary>Reembolso do tutor que cancela em cima da hora (os outros 50% seguem o 90/10 normal).</summary>
    public const int LateCancellationRefundPercentage = 50;

    /// <summary>Prazo do anfitrião para aceitar ou recusar o pedido.</summary>
    public static readonly TimeSpan ResponseWindow = TimeSpan.FromHours(24);

    /// <summary>Antecedência mínima para o tutor cancelar com 100% de volta.</summary>
    public static readonly TimeSpan FullRefundNotice = TimeSpan.FromHours(48);

    private readonly List<BookingPet> _pets = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Booking()
    {
        Period = null!;
    }

    private Booking(Guid ownerId, BookableListing listing, StayPeriod period, IReadOnlyCollection<Guid> petIds, DateTimeOffset now)
        : base(BookingId.New())
    {
        OwnerId = ownerId;
        ListingId = listing.Id;
        HostId = listing.HostId;
        Period = period;
        _pets.AddRange(petIds.Select(petId => new BookingPet(petId)));
        PetCount = petIds.Count;
        NightlyRate = listing.NightlyRate;
        TotalPrice = Math.Round(listing.NightlyRate * period.Nights * petIds.Count, 2, MidpointRounding.AwayFromZero);
        Status = BookingStatus.Requested;
        RequestedAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Tutor que pediu (<c>owner.owners.id</c>).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Cantinho reservado (<c>listing.listings.id</c>).</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Anfitrião dono do cantinho (<c>host.hosts.id</c>), copiado no pedido.</summary>
    public Guid HostId { get; private set; }

    public StayPeriod Period { get; private set; }

    /// <summary>Os pets hospedados, todos do mesmo tutor.</summary>
    public IReadOnlyList<BookingPet> Pets => _pets.AsReadOnly();

    /// <summary>Quantos pets (igual ao tamanho de <see cref="Pets"/>).</summary>
    public int PetCount { get; private set; }

    /// <summary>Cópia da diária por pet no momento do pedido, em reais.</summary>
    public decimal NightlyRate { get; private set; }

    /// <summary>Diária × noites × pets, em reais. O tutor paga exatamente isto.</summary>
    public decimal TotalPrice { get; private set; }

    public BookingStatus Status { get; private set; }

    /// <summary>Quando o tutor pediu. Base do prazo de resposta de 24h.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Quando o anfitrião aceitou ou recusou.</summary>
    public DateTimeOffset? RespondedAt { get; private set; }

    /// <summary>Quando a reserva foi confirmada (hoje, no aceite; com o pagamento, pelo webhook).</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Quando o anfitrião marcou "chegou".</summary>
    public DateTimeOffset? CheckedInAt { get; private set; }

    /// <summary>Quando o anfitrião marcou "voltou pra casa".</summary>
    public DateTimeOffset? CheckedOutAt { get; private set; }

    /// <summary>Quando expirou sem resposta.</summary>
    public DateTimeOffset? ExpiredAt { get; private set; }

    /// <summary>Quando foi cancelada.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Quem cancelou: o tutor ou o anfitrião.</summary>
    public BookingParty? CancelledBy { get; private set; }

    /// <summary>Motivo do cancelamento, opcional.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Motivo da recusa, opcional.</summary>
    public string? DeclineReason { get; private set; }

    /// <summary>
    /// Quanto do valor volta para o tutor no cancelamento (0 a 100). Nulo quando nada foi pago
    /// (cancelada antes da confirmação) ou quando não foi cancelada. O módulo de pagamento usa.
    /// </summary>
    public int? RefundPercentage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Até quando o anfitrião pode responder ao pedido.</summary>
    public DateTimeOffset ResponseDeadline => RequestedAt + ResponseWindow;

    /// <summary>
    /// Libera o contato (endereço exato e telefone) entre tutor e anfitrião: só depois da
    /// confirmação (escopo, regra 7) — Confirmada, Em andamento ou Concluída. Cancelada não libera.
    /// </summary>
    public bool RevealsContact => Status is BookingStatus.Confirmed or BookingStatus.InProgress or BookingStatus.Completed;

    /// <summary>Ocupa vaga no cantinho (Confirmada ou Em andamento): entra na conta de capacidade.</summary>
    public bool OccupiesCapacity => Status is BookingStatus.Confirmed or BookingStatus.InProgress;

    /// <summary>
    /// Pede uma estadia no cantinho para pets do tutor <paramref name="ownerId"/>. Confere, nesta
    /// ordem: o cantinho pode ser reservado; cada pet é do tutor, está ativo e é de espécie e
    /// porte aceitos; nenhum pet já está em outra reserva no período; o cantinho recebe em todos
    /// os dias do período; e há vaga em todas as noites. Nasce Solicitada.
    /// </summary>
    /// <param name="petIds">Os pets pedidos, sem repetição (o validador garante).</param>
    /// <param name="pets">Os pets que existem entre <paramref name="petIds"/>, lidos do módulo Pets.</param>
    /// <param name="petsBookedElsewhere">Quais dos pets já estão em outra reserva em aberto que divide noite com o período.</param>
    /// <param name="occupancy">Quem já ocupa vaga no cantinho no período.</param>
    public static Result<Booking> Request(
        Guid ownerId,
        BookableListing listing,
        StayPeriod period,
        IReadOnlyCollection<Guid> petIds,
        IReadOnlyCollection<BookablePet> pets,
        IReadOnlyCollection<Guid> petsBookedElsewhere,
        ListingOccupancy occupancy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(petIds);
        ArgumentNullException.ThrowIfNull(pets);
        ArgumentNullException.ThrowIfNull(petsBookedElsewhere);
        ArgumentNullException.ThrowIfNull(occupancy);

        if (ownerId == Guid.Empty)
            throw new ArgumentException("The booking must belong to an owner.", nameof(ownerId));

        if (petIds.Count is 0 or > MaxPets)
            throw new ArgumentException($"A booking has 1 to {MaxPets} pets.", nameof(petIds));

        if (!listing.IsBookable)
            return Result<Booking>.Failure(BookingsErrors.ListingNotFound(listing.Id));

        foreach (var petId in petIds)
        {
            var pet = pets.FirstOrDefault(candidate => candidate.Id == petId);

            var eligible = EnsurePetCanStay(ownerId, petId, pet, listing, petsBookedElsewhere);
            if (eligible.IsFailure)
                return Result<Booking>.FromFailure(eligible);
        }

        if (listing.FirstUnavailableDay(period) is { } closedDay)
            return Result<Booking>.Failure(BookingsErrors.WeekdayNotAvailable(closedDay));

        var fits = occupancy.EnsureFits(period, petIds.Count, listing.Capacity);
        if (fits.IsFailure)
            return Result<Booking>.FromFailure(fits);

        return Result<Booking>.Success(new Booking(ownerId, listing, period, petIds, now));
    }

    private static Result EnsurePetCanStay(
        Guid ownerId,
        Guid petId,
        BookablePet? pet,
        BookableListing listing,
        IReadOnlyCollection<Guid> petsBookedElsewhere)
    {
        // Pet de outro tutor (ou de anfitrião) responde igual a pet inexistente (§8).
        if (pet is null || pet.OwnerId != ownerId)
            return Result.Failure(BookingsErrors.PetNotFound(petId));

        if (!pet.IsActive)
            return Result.Failure(BookingsErrors.PetInactive(pet.Name));

        if (!listing.AcceptsSpecies(pet.Species))
            return Result.Failure(BookingsErrors.SpeciesNotAccepted(pet.Name, pet.Species));

        if (!listing.AcceptsSize(pet.Size))
            return Result.Failure(BookingsErrors.SizeNotAccepted(pet.Name, pet.Size ?? string.Empty));

        if (petsBookedElsewhere.Contains(petId))
            return Result.Failure(BookingsErrors.PetPeriodConflict(pet.Name));

        return Result.Success();
    }

    /// <summary>A reserva é do tutor <paramref name="ownerId"/>?</summary>
    public bool IsRequestedBy(Guid ownerId) => OwnerId == ownerId;

    /// <summary>A reserva é num cantinho do anfitrião <paramref name="hostId"/>?</summary>
    public bool IsHostedBy(Guid hostId) => HostId == hostId;

    /// <summary>
    /// Venceu o prazo de resposta em <paramref name="now"/>? Pedido sem resposta vence em 24h ou
    /// quando a data de chegada passa, o que vier primeiro.
    /// </summary>
    public bool IsResponseOverdue(DateTimeOffset now) =>
        Status == BookingStatus.Requested
        && (now > ResponseDeadline || BookingClock.Today(now) > Period.CheckIn);

    /// <summary>
    /// O anfitrião aceita. Só pedido Solicitado e dentro do prazo; a capacidade é conferida de
    /// novo (outra reserva pode ter sido confirmada depois do pedido). Sem o módulo de
    /// pagamento, aceitar já confirma: a reserva passa a ocupar vaga.
    /// </summary>
    /// <param name="listing">As condições atuais do cantinho (a capacidade pode ter mudado).</param>
    /// <param name="occupancy">Quem já ocupa vaga no cantinho no período.</param>
    public Result Accept(BookableListing listing, ListingOccupancy occupancy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(occupancy);

        if (Status != BookingStatus.Requested)
            return Result.Failure(BookingsErrors.NotAwaitingResponse);

        if (IsResponseOverdue(now))
            return Result.Failure(BookingsErrors.ResponseExpired);

        var fits = occupancy.EnsureFits(Period, PetCount, listing.Capacity);
        if (fits.IsFailure)
            return fits;

        Status = BookingStatus.Confirmed;
        RespondedAt = now;
        ConfirmedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>O anfitrião recusa. Só pedido Solicitado e dentro do prazo.</summary>
    public Result Decline(string? reason, DateTimeOffset now)
    {
        if (Status != BookingStatus.Requested)
            return Result.Failure(BookingsErrors.NotAwaitingResponse);

        if (IsResponseOverdue(now))
            return Result.Failure(BookingsErrors.ResponseExpired);

        Status = BookingStatus.Declined;
        DeclineReason = Normalize(reason);
        RespondedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Fecha o pedido que ficou sem resposta no prazo. Devolve se expirou (pedido já respondido
    /// ou ainda no prazo não muda).
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!IsResponseOverdue(now))
            return false;

        Status = BookingStatus.Expired;
        ExpiredAt = now;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    /// Cancela, pelo tutor ou pelo anfitrião. Vale até o check-in (Solicitada, Aceita ou
    /// Confirmada); depois que o pet chegou, não (escopo, seção 6). O reembolso só existe se
    /// a reserva estava Confirmada (paga): anfitrião cancelando devolve 100%; tutor, 100% com
    /// <see cref="FullRefundNotice"/> ou mais de antecedência da chegada (meia-noite do dia, horário
    /// de Brasília) e <see cref="LateCancellationRefundPercentage"/>% depois disso.
    /// </summary>
    public Result Cancel(BookingParty by, string? reason, DateTimeOffset now)
    {
        switch (Status)
        {
            case BookingStatus.Cancelled:
                return Result.Failure(BookingsErrors.AlreadyCancelled);
            case BookingStatus.InProgress or BookingStatus.Completed:
                return Result.Failure(BookingsErrors.CannotCancelAfterCheckIn);
            case BookingStatus.Declined or BookingStatus.Expired:
                return Result.Failure(BookingsErrors.AlreadyClosed);
        }

        RefundPercentage = Status == BookingStatus.Confirmed ? RefundFor(by, now) : null;
        Status = BookingStatus.Cancelled;
        CancelledBy = by;
        CancellationReason = Normalize(reason);
        CancelledAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Quanto volta para o tutor se <paramref name="by"/> cancelar uma reserva paga em <paramref name="now"/>.</summary>
    public int RefundFor(BookingParty by, DateTimeOffset now) =>
        by == BookingParty.Host || now <= BookingClock.StartOf(Period.CheckIn) - FullRefundNotice
            ? FullRefundPercentage
            : LateCancellationRefundPercentage;

    /// <summary>
    /// O anfitrião marca "chegou": o pet está com ele. Só reserva Confirmada, a partir do dia de
    /// chegada e antes do dia de saída.
    /// </summary>
    public Result CheckIn(DateTimeOffset now)
    {
        if (Status != BookingStatus.Confirmed)
            return Result.Failure(BookingsErrors.NotConfirmed);

        var today = BookingClock.Today(now);

        if (today < Period.CheckIn)
            return Result.Failure(BookingsErrors.CheckInTooEarly(Period.CheckIn));

        if (today >= Period.CheckOut)
            return Result.Failure(BookingsErrors.CheckInWindowClosed);

        Status = BookingStatus.InProgress;
        CheckedInAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// O anfitrião marca "voltou pra casa": o pet está com o tutor. Só depois do check-in; pode
    /// ser antes do dia de saída (o tutor buscou mais cedo). Libera repasse e avaliação.
    /// </summary>
    public Result CheckOut(DateTimeOffset now)
    {
        if (Status != BookingStatus.InProgress)
            return Result.Failure(BookingsErrors.NotInProgress);

        Status = BookingStatus.Completed;
        CheckedOutAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Espaços nas pontas saem; texto vazio vira <c>null</c>.</summary>
    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
