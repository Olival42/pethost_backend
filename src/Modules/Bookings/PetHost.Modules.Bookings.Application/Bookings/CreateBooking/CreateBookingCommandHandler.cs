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
/// ocupa vaga. Quem decide é o aceite, que confere de novo com o cantinho travado. Tutor com
/// parcela em atraso não pede: <c>422 BOOKING_OWNER_PAYMENT_OVERDUE</c>. A mesma pessoa não reserva
/// o próprio cantinho (contas de tutor e de anfitrião com o mesmo e-mail): <c>422 BOOKING_OWN_LISTING</c>
/// — pagaria a si mesma e depois se avaliaria. Gravado o pedido, o anfitrião recebe um e-mail com o
/// prazo para responder.
/// </remarks>
public sealed class CreateBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    IBookingListings bookingListings,
    IBookingPets bookingPets,
    IBookingPayments bookingPayments,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
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

        // Estadia já feita e parcela vencida sem pagar: paga antes de pedir outra.
        if (await bookingPayments.HasOverduePaymentAsync(owner.Value!.Id, cancellationToken).ConfigureAwait(false))
            return Result<BookingResponse>.Failure(BookingsErrors.OwnerPaymentOverdue);

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

        if (await BookingAccess.IsSamePersonAsync(bookingParties, owner.Value!.Id, listing.HostId, cancellationToken).ConfigureAwait(false))
            return Result<BookingResponse>.Failure(BookingsErrors.OwnListing);

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

        var response = await responseBuilder.BuildAsync(booking.Value!, BookingParty.Owner, cancellationToken).ConfigureAwait(false);

        // O anfitrião tem 24h para responder: avisa por e-mail, sem contato do tutor (só depois do pagamento).
        await notifications.RequestedAsync(booking.Value!, response, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(response);
    }
}
