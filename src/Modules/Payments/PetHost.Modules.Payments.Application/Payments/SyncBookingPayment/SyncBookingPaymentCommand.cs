using FluentValidation;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.SyncBookingPayment;

/// <summary>
/// Relê agora no Stripe as cobranças da reserva e reconcilia — o app chama ao voltar do Payment
/// Element, sem esperar o webhook (útil também sem a Stripe CLI, em desenvolvimento).
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>admin</c>.</param>
/// <param name="BookingId">Vem da rota.</param>
public sealed record SyncBookingPaymentCommand(Guid UserId, string Role, Guid BookingId) : ICommand<PaymentResponse>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class SyncBookingPaymentCommandValidator : AbstractValidator<SyncBookingPaymentCommand>
{
    public SyncBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");
    }
}

/// <summary>
/// Mesma regra de acesso da leitura. Sem pagamento: <c>404 PAYMENT_NOT_FOUND</c>. Stripe fora do
/// ar: <c>503 PAYMENT_STRIPE_UNAVAILABLE</c> (nada é gravado).
/// </summary>
public sealed class SyncBookingPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentBookings bookings,
    IPaymentParties parties,
    PaymentReconciler reconciler,
    PaymentCommitter committer,
    TimeProvider timeProvider) : ICommandHandler<SyncBookingPaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(SyncBookingPaymentCommand command, CancellationToken cancellationToken)
    {
        var access = await PaymentAccess
            .LoadAsync(bookings, parties, command.UserId, command.Role, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (access.IsFailure)
            return Result<PaymentResponse>.FromFailure(access);

        var (booking, viewer) = access.Value!;

        var payment = await paymentRepository.GetByBookingIdAsync(booking.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
            return Result<PaymentResponse>.Failure(PaymentsErrors.NotFound(booking.Id));

        var effects = new PaymentEffects();

        foreach (var installment in PaymentReconciler.NeedingRefresh(payment, everything: true))
        {
            var refreshed = await reconciler.RefreshAsync(payment, installment, effects, cancellationToken).ConfigureAwait(false);
            if (refreshed.IsFailure)
                return Result<PaymentResponse>.FromFailure(refreshed);
        }

        var reconciled = await reconciler.ReconcileAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (reconciled.IsFailure)
            return Result<PaymentResponse>.FromFailure(reconciled);

        var committed = await committer.CommitAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (committed.IsFailure)
            return Result<PaymentResponse>.FromFailure(committed);

        // A reserva pode ter mudado na reconciliação (confirmada pelo pagamento).
        var current = await bookings.GetAsync(booking.Id, cancellationToken).ConfigureAwait(false) ?? booking;

        return Result<PaymentResponse>.Success(PaymentResponseMapper.ToResponse(payment, current, viewer, timeProvider.GetUtcNow()));
    }
}
