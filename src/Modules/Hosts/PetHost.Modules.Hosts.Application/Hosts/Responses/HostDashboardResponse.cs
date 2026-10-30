using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Hosts.Application.Hosts.Responses;

/// <summary>
/// Painel do anfitrião: pedidos novos, estadias em andamento, próximas estadias e quanto recebeu no
/// mês. As listas trazem só as primeiras (<see cref="HostDashboardBookingsResponse.Count"/> é o
/// total); as completas ficam em <c>GET /bookings/me?status=...</c>. Valores em reais. As reservas
/// são sempre as de agora; só os ganhos (<see cref="Earnings"/>) dependem do mês pedido.
/// </summary>
/// <param name="GeneratedAt">Quando o painel foi montado.</param>
/// <param name="PendingRequests">Pedidos esperando resposta, do prazo mais curto para o mais longo.</param>
/// <param name="AwaitingPayment">Aceitas esperando o tutor pagar (só o total).</param>
/// <param name="InProgress">Pets em casa agora, da saída mais próxima.</param>
/// <param name="OverdueCheckIns">
/// Estadias pagas com a chegada já passada e sem check-in, da mais antiga: marque "Chegou" ou, se o
/// pet não veio, a plataforma cancela no dia de saída.
/// </param>
/// <param name="UpcomingStays">Estadias pagas com chegada hoje ou depois, da chegada mais próxima.</param>
public sealed record HostDashboardResponse(
    DateTimeOffset GeneratedAt,
    HostDashboardBookingsResponse PendingRequests,
    HostDashboardCountResponse AwaitingPayment,
    HostDashboardBookingsResponse InProgress,
    HostDashboardBookingsResponse OverdueCheckIns,
    HostDashboardBookingsResponse UpcomingStays,
    HostDashboardEarningsResponse Earnings)
{
    public static HostDashboardResponse From(
        DateTimeOffset generatedAt,
        DateOnly month,
        HostBookingsOverview bookings,
        HostEarningsSummary earnings,
        int completedStays,
        bool payoutsEnabled)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(earnings);

        return new HostDashboardResponse(
            generatedAt,
            HostDashboardBookingsResponse.From(bookings.PendingRequestsCount, bookings.PendingRequests),
            new HostDashboardCountResponse(bookings.AwaitingPaymentCount),
            HostDashboardBookingsResponse.From(bookings.InProgressCount, bookings.InProgress),
            HostDashboardBookingsResponse.From(bookings.OverdueCheckInsCount, bookings.OverdueCheckIns),
            HostDashboardBookingsResponse.From(bookings.UpcomingStaysCount, bookings.UpcomingStays),
            HostDashboardEarningsResponse.From(month, earnings, completedStays, payoutsEnabled));
    }
}

/// <summary>Um grupo de reservas: o total e as primeiras.</summary>
public sealed record HostDashboardBookingsResponse(int Count, IReadOnlyList<HostDashboardBookingResponse> Items)
{
    public static HostDashboardBookingsResponse From(int count, IReadOnlyList<HostBookingSummary> items) =>
        new(count, [.. items.Select(HostDashboardBookingResponse.From)]);
}

/// <summary>Um grupo de reservas mostrado só pelo total.</summary>
public sealed record HostDashboardCountResponse(int Count);

/// <summary>Uma reserva no painel. Sem telefone nem endereço do tutor.</summary>
/// <param name="OwnerRating">Nota do tutor (média com 1 casa e contagem); omitida sem avaliação.</param>
/// <param name="ResponseDeadline">Até quando responder. Só nos pedidos.</param>
/// <param name="CheckedInAt">Quando o pet chegou. Só nas estadias em andamento.</param>
public sealed record HostDashboardBookingResponse(
    Guid BookingId,
    HostDashboardListingResponse Listing,
    HostDashboardOwnerResponse Owner,
    HostDashboardOwnerRatingResponse? OwnerRating,
    IReadOnlyList<HostDashboardPetResponse> Pets,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int PetCount,
    decimal TotalPrice,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResponseDeadline,
    DateTimeOffset? CheckedInAt)
{
    public static HostDashboardBookingResponse From(HostBookingSummary booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new HostDashboardBookingResponse(
            booking.Id,
            new HostDashboardListingResponse(booking.ListingId, booking.ListingTitle, booking.ListingNeighborhood),
            new HostDashboardOwnerResponse(booking.OwnerId, booking.OwnerName, booking.OwnerAvatarUrl),
            booking.OwnerRating is { } rating ? new HostDashboardOwnerRatingResponse(rating.Average, rating.Count) : null,
            [.. booking.Pets.Select(p => new HostDashboardPetResponse(p.Id, p.Name, p.Species, p.Size))],
            booking.CheckIn,
            booking.CheckOut,
            booking.Nights,
            booking.PetCount,
            booking.TotalPrice,
            booking.RequestedAt,
            booking.ResponseDeadline,
            booking.CheckedInAt);
    }
}

/// <summary>O cantinho: título e bairro (nunca a rua).</summary>
public sealed record HostDashboardListingResponse(Guid Id, string? Title, string? Neighborhood);

/// <summary>O tutor: nome e foto.</summary>
public sealed record HostDashboardOwnerResponse(Guid Id, string? Name, string? AvatarUrl);

/// <summary>A nota do tutor: média com 1 casa e quantas avaliações.</summary>
public sealed record HostDashboardOwnerRatingResponse(decimal Average, int Count);

/// <summary>Um pet da reserva: nome, espécie e porte (nulos se a ficha sumiu).</summary>
public sealed record HostDashboardPetResponse(Guid Id, string? Name, string? Species, string? Size);

/// <summary>Quanto o anfitrião recebeu no mês pedido, contado pela data do repasse (horário de Brasília).</summary>
/// <param name="Month">O mês de <paramref name="Received"/>, <paramref name="Reversed"/>, <paramref name="Net"/>, <paramref name="Breakdown"/> e <paramref name="CompletedStays"/> (<c>yyyy-MM</c>; padrão: o atual).</param>
/// <param name="Received">Repassado no mês, antes de estornos.</param>
/// <param name="Reversed">Estornado no mês (contestação perdida depois do repasse).</param>
/// <param name="Net">Repassado menos estornado no mês.</param>
/// <param name="ReceivedPreviousMonth">Repassado no mês anterior a <paramref name="Month"/>, antes de estornos.</param>
/// <param name="ReceivedYearToDate">Repassado de 1º de janeiro até o fim de <paramref name="Month"/>, antes de estornos.</param>
/// <param name="CompletedStays">Estadias concluídas (check-out) no mês.</param>
/// <param name="Breakdown">Como se chega em <paramref name="Received"/>.</param>
/// <param name="Held">Guardado agora para repassar, pelo motivo. Não depende de <paramref name="Month"/>.</param>
/// <param name="Expected">O que ainda vai entrar das parcelas por pagar. Não depende de <paramref name="Month"/>.</param>
/// <param name="PayoutsEnabled">O Stripe libera os repasses. <c>false</c>: o dinheiro fica guardado até liberar.</param>
public sealed record HostDashboardEarningsResponse(
    string Month,
    decimal Received,
    decimal Reversed,
    decimal Net,
    decimal ReceivedPreviousMonth,
    decimal ReceivedYearToDate,
    int CompletedStays,
    HostDashboardEarningsBreakdownResponse Breakdown,
    HostDashboardHeldResponse Held,
    HostDashboardExpectedResponse Expected,
    bool PayoutsEnabled)
{
    public static HostDashboardEarningsResponse From(
        DateOnly month,
        HostEarningsSummary earnings,
        int completedStays,
        bool payoutsEnabled)
    {
        ArgumentNullException.ThrowIfNull(earnings);

        return new HostDashboardEarningsResponse(
            $"{month.Year:D4}-{month.Month:D2}",
            Reais(earnings.TransferredCents),
            Reais(earnings.ReversedCents),
            Reais(earnings.TransferredCents - earnings.ReversedCents),
            Reais(earnings.PreviousMonthTransferredCents),
            Reais(earnings.YearToDateTransferredCents),
            completedStays,
            new HostDashboardEarningsBreakdownResponse(
                Reais(earnings.Breakdown.PaidByOwnersCents),
                Reais(earnings.Breakdown.LateFeeCents),
                Reais(earnings.Breakdown.RefundedCents),
                Reais(earnings.Breakdown.PlatformFeeCents)),
            new HostDashboardHeldResponse(
                Reais(earnings.Held.TotalCents),
                Reais(earnings.Held.AwaitingCheckOutCents),
                Reais(earnings.Held.DisputedCents),
                Reais(earnings.Held.BlockedCents)),
            new HostDashboardExpectedResponse(Reais(earnings.Expected.Cents), earnings.Expected.Installments),
            payoutsEnabled);
    }

    /// <summary>Centavos (como o Stripe) para reais (como a reserva).</summary>
    private static decimal Reais(long cents) => cents / 100m;
}

/// <summary>
/// As parcelas repassadas no mês: <c>paidByOwners − refunded − platformFee = received</c>.
/// </summary>
/// <param name="PaidByOwners">O que os tutores pagaram nessas parcelas, multa e juros incluídos.</param>
/// <param name="LateFees">Multa e juros por atraso (já dentro de <paramref name="PaidByOwners"/>).</param>
/// <param name="Refunded">Devolvido ao tutor (cancelamento com reembolso parcial).</param>
/// <param name="PlatformFee">Os 10% do PetHost.</param>
public sealed record HostDashboardEarningsBreakdownResponse(
    decimal PaidByOwners,
    decimal LateFees,
    decimal Refunded,
    decimal PlatformFee);

/// <summary>Guardado agora: os 90% previstos das parcelas pagas ainda não repassadas.</summary>
/// <param name="AwaitingCheckOut">A estadia ainda não acabou: sai no check-out.</param>
/// <param name="Disputed">Contestação aberta no cartão: espera o Stripe decidir.</param>
/// <param name="Blocked">O repasse não passou (Stripe bloqueou ou recusou) ou o reembolso falhou: a manutenção tenta de novo.</param>
public sealed record HostDashboardHeldResponse(
    decimal Total,
    decimal AwaitingCheckOut,
    decimal Disputed,
    decimal Blocked);

/// <summary>Parcelas que os tutores ainda vão pagar nas reservas com pagamento em andamento.</summary>
/// <param name="Amount">Os 90% previstos (sem multa por atraso).</param>
/// <param name="Installments">Quantas parcelas.</param>
public sealed record HostDashboardExpectedResponse(decimal Amount, int Installments);
