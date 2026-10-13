using FluentValidation;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;

/// <summary>
/// Uma rodada da tarefa em segundo plano: pedidos sem resposta há mais de 24h (ou cuja data de
/// chegada já passou) viram <c>expired</c>. Disparada sem usuário.
/// </summary>
public sealed record ExpireOverdueBookingsCommand : ICommand<ExpireOverdueBookingsResponse>
{
    /// <summary>Quantos pedidos uma rodada fecha, no máximo; o resto fica para a próxima.</summary>
    public const int BatchSize = 200;
}

/// <summary>Quantos pedidos expiraram nesta rodada.</summary>
public sealed record ExpireOverdueBookingsResponse(int Expired);

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class ExpireOverdueBookingsCommandValidator : AbstractValidator<ExpireOverdueBookingsCommand>;

public sealed class ExpireOverdueBookingsCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>
{
    public async Task<Result<ExpireOverdueBookingsResponse>> HandleAsync(
        ExpireOverdueBookingsCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var overdue = await bookingRepository
            .ListOverdueRequestsAsync(now - Booking.ResponseWindow, BookingClock.Today(now), ExpireOverdueBookingsCommand.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        var expired = overdue.Where(booking => booking.Expire(now)).ToList();
        if (expired.Count == 0)
            return Result<ExpireOverdueBookingsResponse>.Success(new ExpireOverdueBookingsResponse(0));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<ExpireOverdueBookingsResponse>.FromFailure(saved);

        foreach (var booking in expired)
            await BookingTrail.RecordAsync(auditTrail, AuditActions.BookingExpired, booking, cancellationToken).ConfigureAwait(false);

        return Result<ExpireOverdueBookingsResponse>.Success(new ExpireOverdueBookingsResponse(expired.Count));
    }
}
