using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.AcceptBooking;

/// <summary>
/// O anfitrião aceita o pedido. A reserva fica <c>accepted</c>: segura a vaga e o tutor tem 24h
/// para pagar a 1ª parcela (quem confirma é o pagamento).
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
public sealed record AcceptBookingCommand(Guid UserId, Guid BookingId) : ICommand<BookingResponse>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class AcceptBookingCommandValidator : AbstractValidator<AcceptBookingCommand>
{
    public AcceptBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");
    }
}

/// <summary>
/// Só o anfitrião do cantinho, ativo. A capacidade é conferida com o cantinho travado: dois
/// aceites ao mesmo tempo não passam juntos do limite. Prazo vencido: a reserva expira e o
/// aceite volta <c>422 BOOKING_RESPONSE_EXPIRED</c>. Tutor suspenso ou inativo desde o pedido:
/// <c>422 BOOKING_OWNER_INACTIVE</c>, sem gravar. Avisa o tutor por e-mail (aceita, com o prazo
/// para pagar); o anfitrião recebe "confirmada" quando o tutor pagar.
/// </summary>
public sealed class AcceptBookingCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingParties bookingParties,
    IBookingListings bookingListings,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<AcceptBookingCommand, BookingResponse>
{
    public async Task<Result<BookingResponse>> HandleAsync(AcceptBookingCommand command, CancellationToken cancellationToken)
    {
        var loaded = await BookingAccess
            .LoadForHostAsync(bookingRepository, bookingParties, command.UserId, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<BookingResponse>.FromFailure(loaded);

        var booking = loaded.Value!;
        var now = timeProvider.GetUtcNow();

        if (booking.Expire(now))
            return await ExpireAsync(booking, cancellationToken).ConfigureAwait(false);

        // Tutor suspenso depois do pedido: aceitar mandaria a ele o endereço exato do anfitrião.
        // Outro status cai no erro do próprio Accept.
        if (booking.Status == BookingStatus.Requested
            && !await bookingParties.IsOwnerAvailableAsync(booking.OwnerId, cancellationToken).ConfigureAwait(false))
        {
            return Result<BookingResponse>.Failure(BookingsErrors.OwnerInactive);
        }

        var listing = await bookingListings.FindAsync(booking.ListingId, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return Result<BookingResponse>.Failure(BookingsErrors.ListingNotFound(booking.ListingId));

        var accepted = await unitOfWork
            .RunLockedOnListingAsync(
                booking.ListingId,
                async ct =>
                {
                    var occupying = await bookingRepository.ListOccupyingAsync(booking.ListingId, booking.Period, ct).ConfigureAwait(false);

                    var result = booking.Accept(listing, ListingOccupancy.Of(occupying), now);
                    if (result.IsFailure)
                        return result;

                    return await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (accepted.IsFailure)
            return Result<BookingResponse>.FromFailure(accepted);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingAccepted, booking, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder.BuildAsync(booking, BookingParty.Host, cancellationToken).ConfigureAwait(false);
        await notifications.AcceptedAsync(booking, response, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Success(response);
    }

    /// <summary>Grava a expiração (o pedido ficou sem resposta no prazo) e devolve o erro.</summary>
    private async Task<Result<BookingResponse>> ExpireAsync(Booking booking, CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<BookingResponse>.FromFailure(saved);

        await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingExpired, booking, cancellationToken).ConfigureAwait(false);

        return Result<BookingResponse>.Failure(BookingsErrors.ResponseExpired);
    }
}
