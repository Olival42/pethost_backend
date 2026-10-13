using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.ListMyBookings;

/// <summary>
/// As reservas da conta logada: para o tutor, as que ele pediu ("Minhas estadias"); para o
/// anfitrião, as dos cantinhos dele ("Pedidos" e "Estadias").
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="Statuses">Só estes status (ex.: <c>requested</c>); vazio = todos.</param>
/// <param name="Page">Começa em 1.</param>
public sealed record ListMyBookingsQuery(
    Guid UserId,
    string Role,
    IReadOnlyList<string>? Statuses = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<BookingResponse>>;

/// <summary>
/// Paginada (§13), da chegada mais recente para a mais antiga. Conta sem o perfil do papel:
/// <c>404 BOOKING_OWNER_NOT_FOUND</c> / <c>BOOKING_HOST_NOT_FOUND</c>. Perfil inativo continua
/// vendo o histórico.
/// </summary>
public sealed class ListMyBookingsQueryHandler(
    IBookingRepository bookingRepository,
    IBookingParties bookingParties,
    BookingResponseBuilder responseBuilder) : IQueryHandler<ListMyBookingsQuery, PagedResult<BookingResponse>>
{
    public async Task<Result<PagedResult<BookingResponse>>> HandleAsync(ListMyBookingsQuery query, CancellationToken cancellationToken)
    {
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

        var isHost = query.Role == Roles.Host;

        var party = isHost
            ? await BookingAccess.ResolveHostAsync(bookingParties, query.UserId, requireAvailable: false, cancellationToken).ConfigureAwait(false)
            : await BookingAccess.ResolveOwnerAsync(bookingParties, query.UserId, requireAvailable: false, cancellationToken).ConfigureAwait(false);

        if (party.IsFailure)
            return Result<PagedResult<BookingResponse>>.FromFailure(party);

        var distinct = statuses.Distinct().ToList();
        var search = isHost
            ? BookingSearch.ForHost(party.Value!.Id, distinct, query.Page, query.PageSize)
            : BookingSearch.ForOwner(party.Value!.Id, distinct, query.Page, query.PageSize);

        var (items, total) = await bookingRepository.SearchAsync(search, cancellationToken).ConfigureAwait(false);
        var responses = await responseBuilder.BuildAsync(items, isHost ? BookingParty.Host : BookingParty.Owner, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<BookingResponse>>.Success(
            new PagedResult<BookingResponse>(responses, query.Page, query.PageSize, total));
    }
}
