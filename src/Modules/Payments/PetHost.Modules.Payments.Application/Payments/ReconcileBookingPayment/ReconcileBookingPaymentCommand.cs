using FluentValidation;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.ReconcileBookingPayment;

/// <summary>
/// O Bookings avisou que a reserva mudou (cancelada, expirada, concluída): o pagamento faz o que
/// falta — devolve, cancela as cobranças em aberto, repassa ao anfitrião. Disparada sem usuário,
/// pelo contrato <c>IBookingPaymentsSync</c>.
/// </summary>
public sealed record ReconcileBookingPaymentCommand(Guid BookingId) : ICommand<Unit>;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class ReconcileBookingPaymentCommandValidator : AbstractValidator<ReconcileBookingPaymentCommand>
{
    public ReconcileBookingPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");
    }
}

/// <summary>
/// Reserva sem pagamento (cancelada antes de o tutor escolher as parcelas): nada a fazer. O que
/// falhar no Stripe fica para a manutenção periódica.
/// </summary>
public sealed class ReconcileBookingPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    PaymentReconciler reconciler,
    PaymentCommitter committer) : ICommandHandler<ReconcileBookingPaymentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReconcileBookingPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await paymentRepository.GetByBookingIdAsync(command.BookingId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
            return Result<Unit>.Success(Unit.Value);

        var effects = new PaymentEffects();

        var reconciled = await reconciler.ReconcileAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (reconciled.IsFailure)
            return Result<Unit>.FromFailure(reconciled);

        var committed = await committer.CommitAsync(payment, effects, cancellationToken).ConfigureAwait(false);

        return committed.IsSuccess ? Result<Unit>.Success(Unit.Value) : Result<Unit>.FromFailure(committed);
    }
}
