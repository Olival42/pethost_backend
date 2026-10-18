using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.ListBookings;

/// <summary>
/// Todas as reservas, para o admin: achar a reserva de uma pendência, de uma denúncia ou de um
/// atendimento sem ter o id em mãos. Os filtros se somam; sem filtro, todas.
/// </summary>
/// <param name="Statuses">Só estes status (ex.: <c>requested</c>); vazio = todos.</param>
/// <param name="OwnerId">Só as pedidas por este tutor (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Só as dos cantinhos deste anfitrião (<c>host.hosts.id</c>).</param>
/// <param name="ListingId">Só as deste cantinho.</param>
/// <param name="Page">Começa em 1.</param>
public sealed record ListBookingsQuery(
    IReadOnlyList<string>? Statuses = null,
    Guid? OwnerId = null,
    Guid? HostId = null,
    Guid? ListingId = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<BookingResponse>>;

/// <summary>
/// Paginada (§13), da chegada mais recente para a mais antiga, como as listas "minhas". O admin vê
/// a reserva como no detalhe: sem o contato de nenhum lado, com a nota do tutor.
/// </summary>
public sealed class ListBookingsQueryHandler(
    IBookingRepository bookingRepository,
    BookingResponseBuilder responseBuilder) : IQueryHandler<ListBookingsQuery, PagedResult<BookingResponse>>
{
    public async Task<Result<PagedResult<BookingResponse>>> HandleAsync(ListBookingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();

        var statuses = new List<BookingStatus>();
        // Aceita o parâmetro repetido (status=a&status=b) ou separado por vírgula (status=a,b).
        var values = (query.Statuses ?? [])
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        foreach (var value in values)
        {
            if (BookingStatusValues.TryParse(value, out var status))
                statuses.Add(status);
            else
                errors.Add(BookingsErrors.StatusFilterInvalid);
        }

        if (query.Page < 1)
            errors.Add(BookingsErrors.PageInvalid);

        if (query.PageSize is < 1 or > BookingsErrors.MaxPageSize)
            errors.Add(BookingsErrors.PageSizeInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<BookingResponse>>.Failure(errors.Distinct());

        var search = new BookingSearch(query.OwnerId, query.HostId, [.. statuses.Distinct()], query.Page, query.PageSize, query.ListingId);

        var (items, total) = await bookingRepository.SearchAsync(search, cancellationToken).ConfigureAwait(false);
        var responses = await responseBuilder.BuildAsync(items, viewer: null, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<BookingResponse>>.Success(
            new PagedResult<BookingResponse>(responses, query.Page, query.PageSize, total));
    }
}
