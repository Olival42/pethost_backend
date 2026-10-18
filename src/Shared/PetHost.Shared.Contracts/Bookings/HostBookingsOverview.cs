namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// As reservas do anfitrião para o painel (<see cref="IBookingDirectory.GetHostOverviewAsync"/>):
/// o total de cada grupo e as primeiras de cada lista.
/// </summary>
/// <param name="PendingRequestsCount">Pedidos esperando resposta (<c>requested</c>, ainda no prazo).</param>
/// <param name="PendingRequests">Os pedidos, do prazo de resposta mais curto para o mais longo.</param>
/// <param name="AwaitingPaymentCount">Aceitas esperando o tutor pagar a 1ª parcela (<c>accepted</c>).</param>
/// <param name="InProgressCount">Estadias em andamento (<c>in_progress</c>): o pet está com o anfitrião.</param>
/// <param name="InProgress">As estadias em andamento, da saída mais próxima para a mais distante.</param>
/// <param name="OverdueCheckInsCount">
/// Estadias pagas (<c>confirmed</c>) com o dia de chegada já passado (horário de Brasília) e sem
/// check-in: o anfitrião esqueceu de marcar "Chegou" ou o pet não veio.
/// </param>
/// <param name="OverdueCheckIns">As chegadas atrasadas, da mais antiga para a mais recente.</param>
/// <param name="UpcomingStaysCount">Estadias pagas que ainda não começaram (<c>confirmed</c>, chegada hoje ou depois).</param>
/// <param name="UpcomingStays">As próximas estadias, da chegada mais próxima para a mais distante.</param>
public sealed record HostBookingsOverview(
    int PendingRequestsCount,
    IReadOnlyList<HostBookingSummary> PendingRequests,
    int AwaitingPaymentCount,
    int InProgressCount,
    IReadOnlyList<HostBookingSummary> InProgress,
    int OverdueCheckInsCount,
    IReadOnlyList<HostBookingSummary> OverdueCheckIns,
    int UpcomingStaysCount,
    IReadOnlyList<HostBookingSummary> UpcomingStays);

/// <summary>Uma reserva no painel do anfitrião. Sem telefone nem endereço do tutor.</summary>
/// <param name="ListingTitle">Título do cantinho (nulo se ele sumiu).</param>
/// <param name="ListingNeighborhood">Bairro do cantinho (nulo se ele sumiu).</param>
/// <param name="OwnerName">Nome do tutor (nulo se a conta sumiu).</param>
/// <param name="OwnerRating">Nota do tutor, do que os anfitriões escreveram dele; nula sem avaliação.</param>
/// <param name="ResponseDeadline">Até quando responder. Só no pedido (<c>requested</c>).</param>
/// <param name="CheckedInAt">Quando o pet chegou. Só na estadia em andamento.</param>
public sealed record HostBookingSummary(
    Guid Id,
    Guid ListingId,
    string? ListingTitle,
    string? ListingNeighborhood,
    Guid OwnerId,
    string? OwnerName,
    string? OwnerAvatarUrl,
    HostBookingOwnerRating? OwnerRating,
    IReadOnlyList<HostBookingPet> Pets,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int PetCount,
    decimal TotalPrice,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ResponseDeadline,
    DateTimeOffset? CheckedInAt);

/// <summary>A nota do tutor: média com 1 casa e quantas avaliações.</summary>
public sealed record HostBookingOwnerRating(decimal Average, int Count);

/// <summary>Um pet da reserva: nome, espécie e porte (nulos se a ficha sumiu).</summary>
public sealed record HostBookingPet(Guid Id, string? Name, string? Species, string? Size);
