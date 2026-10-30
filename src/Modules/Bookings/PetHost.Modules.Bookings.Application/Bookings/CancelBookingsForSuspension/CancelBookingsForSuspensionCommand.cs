using FluentValidation;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings.CancelBookingsForSuspension;

/// <summary>
/// O admin suspendeu o tutor ou o anfitrião: a plataforma cancela as reservas dele que ainda não
/// começaram (Solicitada, Aceita ou Confirmada). Disparada sem usuário pelo módulo Owners ou Hosts
/// (contrato <c>IBookingSuspensions</c>), depois que a suspensão foi gravada.
/// </summary>
/// <param name="Suspended">Quem foi suspenso: <see cref="BookingParty.Owner"/> ou <see cref="BookingParty.Host"/>.</param>
/// <param name="PartyId">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>).</param>
public sealed record CancelBookingsForSuspensionCommand(BookingParty Suspended, Guid PartyId) : ICommand<CancelBookingsForSuspensionResponse>;

/// <summary>Quantas reservas foram canceladas agora e quais não puderam ser (conflito que se repetiu).</summary>
public sealed record CancelBookingsForSuspensionResponse(int Cancelled, IReadOnlyList<Guid> NotCancelled);

public sealed class CancelBookingsForSuspensionCommandValidator : AbstractValidator<CancelBookingsForSuspensionCommand>
{
    public CancelBookingsForSuspensionCommandValidator()
    {
        RuleFor(x => x.Suspended)
            .Must(party => party is BookingParty.Owner or BookingParty.Host).WithError(BookingsErrors.SuspendedPartyInvalid);

        RuleFor(x => x.PartyId)
            .NotEmpty().WithError(BookingsErrors.PartyIdRequired);
    }
}

/// <summary>
/// A regra fica em <see cref="Booking.CancelForSuspension"/>: <c>cancelledBy = platform</c> e, na
/// reserva paga, os 100% para a outra parte (anfitrião suspenso → reembolso de 100% ao tutor; tutor
/// suspenso → sem reembolso, o pago segue o 90/10 para o anfitrião). Cada reserva é gravada
/// sozinha; conflito (alguém mexeu ao mesmo tempo) relê e tenta uma vez mais, e o que ainda falhar
/// volta em <c>NotCancelled</c> (o adaptador registra no log) — suspender de novo completa (é idempotente). Cada cancelada vai para a trilha
/// (<c>cause</c> = <c>owner_suspended</c>/<c>host_suspended</c>), avisa o pagamento (reembolso e
/// cobranças em aberto) e manda e-mail aos dois lados.
/// </summary>
/// <remarks>
/// A estadia em andamento (<c>in_progress</c>) não é cancelada: o pet está com o anfitrião. Com o
/// tutor suspenso, o anfitrião faz o check-out normalmente; com o anfitrião suspenso, o admin faz
/// (<c>POST /bookings/{id}/check-out</c>).
/// </remarks>
public sealed class CancelBookingsForSuspensionCommandHandler(
    IBookingRepository bookingRepository,
    IBookingsUnitOfWork unitOfWork,
    IBookingPayments bookingPayments,
    BookingResponseBuilder responseBuilder,
    BookingNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CancelBookingsForSuspensionCommand, CancelBookingsForSuspensionResponse>
{
    /// <summary>Tentativas por reserva quando outra pessoa a altera ao mesmo tempo.</summary>
    private const int MaxAttempts = 2;

    public async Task<Result<CancelBookingsForSuspensionResponse>> HandleAsync(CancelBookingsForSuspensionCommand command, CancellationToken cancellationToken)
    {
        var ids = await bookingRepository.ListCancellableIdsAsync(command.Suspended, command.PartyId, cancellationToken).ConfigureAwait(false);

        var cancelled = 0;
        var notCancelled = new List<Guid>();
        foreach (var id in ids)
        {
            switch (await CancelAsync(id, command.Suspended, cancellationToken).ConfigureAwait(false))
            {
                case true:
                    cancelled++;
                    break;
                case null:
                    notCancelled.Add(id.Value);
                    break;
            }
        }

        return Result<CancelBookingsForSuspensionResponse>.Success(new CancelBookingsForSuspensionResponse(cancelled, notCancelled));
    }

    /// <summary>Cancelou (<c>true</c>), não havia o que cancelar (<c>false</c>) ou não conseguiu gravar (<c>null</c>).</summary>
    private async Task<bool?> CancelAsync(BookingId id, BookingParty suspended, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (booking is null || !booking.CancelForSuspension(suspended, timeProvider.GetUtcNow()))
                return false;

            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                if (saved.FirstError?.Code == BookingsErrors.Conflict.Code && attempt < MaxAttempts)
                    continue;

                return null;
            }

            var cause = suspended == BookingParty.Host ? HostSuspendedCause : OwnerSuspendedCause;
            await BookingTrail.RecordCancelledAsync(auditTrail, booking, cancellationToken, cause: cause).ConfigureAwait(false);
            await bookingPayments.BookingChangedAsync(booking.Id.Value, cancellationToken).ConfigureAwait(false);

            var response = await responseBuilder.BuildAsync(booking, viewer: null, cancellationToken).ConfigureAwait(false);
            await notifications.CancelledAsync(booking, response, cancellationToken).ConfigureAwait(false);

            return true;
        }

        return null;
    }

    /// <summary>Valor de <c>cause</c> na trilha quando o anfitrião foi suspenso.</summary>
    public const string HostSuspendedCause = "host_suspended";

    /// <summary>Valor de <c>cause</c> na trilha quando o tutor foi suspenso.</summary>
    public const string OwnerSuspendedCause = "owner_suspended";
}
