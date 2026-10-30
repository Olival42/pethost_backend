using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.GetBookingById;

/// <summary>Uma reserva, com o cantinho, os dois lados e os pets.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record GetBookingByIdQuery(Guid UserId, string Role, Guid BookingId) : IQuery<BookingResponse>;

/// <summary>
/// Quem vê: o tutor que pediu, o anfitrião do cantinho e o admin. Qualquer outra conta:
/// <c>404 BOOKING_NOT_FOUND</c>, como se não existisse (§8).
/// </summary>
public sealed class GetBookingByIdQueryHandler(
    IBookingRepository bookingRepository,
    IBookingParties bookingParties,
    BookingResponseBuilder responseBuilder) : IQueryHandler<GetBookingByIdQuery, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(GetBookingByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.Role == Roles.Admin)
        {
            var id = new BookingId(query.BookingId);
            var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

            return booking is null
                ? Result<BookingResponse>.Failure(BookingsErrors.NotFound(id))
                : Result<BookingResponse>.Success(await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false));
        }

        var loaded = await BookingAccess
            .LoadForPartyAsync(bookingRepository, bookingParties, query.UserId, query.Role, query.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        return Result<BookingResponse>.Success(
            await responseBuilder.BuildAsync(loaded.Value!.Booking, loaded.Value.Side, cancellationToken).ConfigureAwait(false));
    }
}
