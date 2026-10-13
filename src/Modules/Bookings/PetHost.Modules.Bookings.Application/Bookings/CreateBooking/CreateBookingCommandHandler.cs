using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CreateBooking;

/// <summary>
/// Cria o pedido. O tutor sai do token; o cantinho precisa poder ser reservado; as regras de
/// pets, dias da semana e capacidade ficam em <see cref="Booking.Request"/>.
/// </summary>
/// <remarks>
/// A capacidade é conferida aqui só para não deixar o tutor pedir o impossível: o pedido não
/// ocupa vaga. Quem decide é o aceite, que confere de novo com o cantinho travado.
/// </remarks>
public sealed class CreateBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    IBookingListings bookingListings,
    IBookingPets bookingPets,
    BookingResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CreateBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(CreateBookingCommand command, CancellationToken cancellationToken)
    {
        var owner = await BookingAccess
            .ResolveOwnerAsync(bookingParties, command.UserId, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (owner.IsFailure)
            return Result<BookingResponse>.FromFailure(owner);

        // O validador já garantiu o formato; a regra do período é conferida de novo (a data de
        // hoje pode ter virado entre a validação e aqui).
        _ = StayPeriod.TryParseDate(command.CheckIn, out var checkIn);
        _ = StayPeriod.TryParseDate(command.CheckOut, out var checkOut);

        var now = timeProvider.GetUtcNow();
        var period = StayPeriod.Create(checkIn, checkOut, BookingClock.Today(now));

        if (period.IsFailure)
            return Result<BookingResponse>.FromFailure(period);

        var listing = await bookingListings.FindAsync(command.ListingId, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return Result<BookingResponse>.Failure(BookingsErrors.ListingNotFound(command.ListingId));

        IReadOnlyList<Guid> petIds = command.PetIds ?? [];
        var pets = await bookingPets.GetAsync(petIds, cancellationToken).ConfigureAwait(false);
        var petsBooked = await bookingRepository.ListPetsBookedAsync(petIds, period.Value!, cancellationToken).ConfigureAwait(false);
        var occupying = await bookingRepository.ListOccupyingAsync(listing.Id, period.Value!, cancellationToken).ConfigureAwait(false);

        var booking = Booking.Request(
            owner.Value!.Id,
            listing,
            period.Value!,
            petIds,
            pets,
            petsBooked,
            ListingOccupancy.Of(occupying),
            now);

        if (booking.IsFailure)
            return Result<BookingResponse>.FromFailure(booking);

        bookingRepository.Add(booking.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingRequested, booking.Value!, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(
            await responseBuilder.BuildAsync(booking.Value!, BookingParty.Owner, cancellationToken).ConfigureAwait(false));
    }
}
