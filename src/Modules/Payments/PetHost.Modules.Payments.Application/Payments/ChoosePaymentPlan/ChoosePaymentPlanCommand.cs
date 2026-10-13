using FluentValidation;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.ChoosePaymentPlan;

/// <summary>
/// O tutor escolhe em quantas parcelas paga a reserva aceita (1 = à vista, até 6). Pode trocar
/// enquanto nada foi pago.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
/// <param name="InstallmentCount">1 a 6; cada parcela de pelo menos R$ 50,00.</param>
public sealed record ChoosePaymentPlanCommand(Guid UserId, Guid BookingId, int InstallmentCount) : ICommand<PaymentResponse>;

public sealed class ChoosePaymentPlanCommandValidator : AbstractValidator<ChoosePaymentPlanCommand>
{
    public ChoosePaymentPlanCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");

        RuleFor(x => x.InstallmentCount)
            .InclusiveBetween(1, Payment.MaxInstallments).WithMessage(PaymentsErrors.InstallmentCountInvalid.Message);
    }
}

/// <summary>
/// Só o tutor que pediu, com a reserva aceita e dentro do prazo da 1ª parcela. Trocar o plano
/// cancela no Stripe as cobranças abertas das parcelas antigas. Erros: reserva ainda não aceita —
/// <c>422 PAYMENT_BOOKING_NOT_AWAITING_PAYMENT</c>; já paga ou em
/// andamento — <c>422 PAYMENT_PLAN_LOCKED</c>; cancelada ou expirada — <c>422 PAYMENT_CLOSED</c>;
/// prazo vencido — <c>422 PAYMENT_WINDOW_CLOSED</c>; parcelas demais para o valor —
/// <c>422 PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED</c>.
/// </summary>
public sealed class ChoosePaymentPlanCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentsUnitOfWork unitOfWork,
    IPaymentBookings bookings,
    IPaymentParties parties,
    IStripePayments stripe,
    PaymentEffectsPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<ChoosePaymentPlanCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(ChoosePaymentPlanCommand command, CancellationToken cancellationToken)
    {
        var access = await PaymentAccess
            .LoadAsync(bookings, parties, command.UserId, Roles.Owner, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (access.IsFailure)
            return Result<PaymentResponse>.FromFailure(access);

        var booking = access.Value!.Booking;
        var now = timeProvider.GetUtcNow();

        var payable = EnsurePayable(booking, now);
        if (payable.IsFailure)
            return Result<PaymentResponse>.FromFailure(payable);

        var deadline = booking.PaymentDeadline!.Value;
        var payment = await paymentRepository.GetByBookingIdAsync(booking.Id, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            var created = Payment.Create(
                new PayableBooking(booking.Id, booking.OwnerId, booking.HostId, booking.AmountCents, deadline),
                command.InstallmentCount,
                now);

            if (created.IsFailure)
                return Result<PaymentResponse>.FromFailure(created);

            payment = created.Value!;
            paymentRepository.Add(payment);
        }
        else
        {
            if (!payment.CanChangePlan)
                return Result<PaymentResponse>.Failure(payment.Status == PaymentStatus.Cancelled ? PaymentsErrors.Closed : PaymentsErrors.PlanLocked);

            // As cobranças das parcelas antigas não podem mais ser pagas.
            foreach (var intent in payment.OpenIntentIds)
            {
                var cancelled = await stripe.CancelIntentAsync(intent, cancellationToken).ConfigureAwait(false);
                if (cancelled.IsFailure)
                {
                    return cancelled.FirstError?.Code == PaymentsErrors.StripeRequestRejected.Code
                        ? Result<PaymentResponse>.Failure(PaymentsErrors.PlanLocked)
                        : Result<PaymentResponse>.FromFailure(cancelled);
                }
            }

            var changed = payment.ChangePlan(command.InstallmentCount, deadline, now);
            if (changed.IsFailure)
                return Result<PaymentResponse>.FromFailure(changed);
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<PaymentResponse>.FromFailure(saved);

        var effects = new PaymentEffects();
        effects.Add(PaymentEffectKind.PlanChosen, 1, payment.AmountCents);
        await publisher.PublishAsync(payment, effects, cancellationToken).ConfigureAwait(false);

        return Result<PaymentResponse>.Success(PaymentResponseMapper.ToResponse(payment, booking, PaymentViewer.Owner));
    }

    /// <summary>Reserva aceita, dentro do prazo da 1ª parcela.</summary>
    private static Result EnsurePayable(PaymentBooking booking, DateTimeOffset now) => booking.Stage switch
    {
        BookingStage.Requested => Result.Failure(PaymentsErrors.BookingNotAwaitingPayment),
        BookingStage.Cancelled or BookingStage.Closed => Result.Failure(PaymentsErrors.Closed),
        BookingStage.Confirmed or BookingStage.InProgress or BookingStage.Completed => Result.Failure(PaymentsErrors.PlanLocked),
        _ when booking.PaymentDeadline is not { } deadline || now > deadline => Result.Failure(PaymentsErrors.PaymentWindowClosed),
        _ => Result.Success(),
    };
}
