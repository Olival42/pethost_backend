using FluentValidation;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.StartInstallmentPayment;

/// <summary>
/// O tutor vai pagar uma parcela: devolve o <c>clientSecret</c> da cobrança no Stripe para o app
/// abrir o Payment Element (cartão e Pix dentro do PetHost). Quem confirma o pagamento é o webhook,
/// nunca o retorno do navegador.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="BookingId">Vem da rota.</param>
/// <param name="InstallmentNumber">Vem da rota (1 a 6).</param>
public sealed record StartInstallmentPaymentCommand(Guid UserId, Guid BookingId, int InstallmentNumber) : ICommand<PaymentSessionResponse>;

public sealed class StartInstallmentPaymentCommandValidator : AbstractValidator<StartInstallmentPaymentCommand>
{
    public StartInstallmentPaymentCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty().WithMessage("Booking id is required.");

        RuleFor(x => x.InstallmentNumber)
            .InclusiveBetween(1, Payment.MaxInstallments).WithMessage(PaymentsErrors.InstallmentNumberInvalid.Message);
    }
}

/// <summary>
/// Só o tutor que pediu. As parcelas são pagas em ordem (adiantar a próxima pode); a 1ª, dentro
/// do prazo. A cobrança aberta da parcela é reaproveitada (renovando o prazo do Pix, se o anterior
/// venceu); cancelada ou inexistente, outra é criada. O QR code do Pix vence no prazo da parcela,
/// para não ser pago depois dele. Parcela que o Stripe já está confirmando ou já confirmou: o
/// pagamento é relido e volta <c>422 PAYMENT_INSTALLMENT_PROCESSING</c> / <c>..._ALREADY_PAID</c>.
/// </summary>
public sealed class StartInstallmentPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentsUnitOfWork unitOfWork,
    IPaymentBookings bookings,
    IPaymentParties parties,
    IStripePayments stripe,
    PaymentReconciler reconciler,
    PaymentEffectsPublisher publisher,
    TimeProvider timeProvider) : ICommandHandler<StartInstallmentPaymentCommand, PaymentSessionResponse>
{
    /// <summary>Os meios aceitos no checkout transparente.</summary>
    public static readonly IReadOnlyList<string> PaymentMethods = [PaymentMethodValues.ToWire(PaymentMethod.Card), PaymentMethodValues.ToWire(PaymentMethod.Pix)];

    public async Task<Result<PaymentSessionResponse>> HandleAsync(StartInstallmentPaymentCommand command, CancellationToken cancellationToken)
    {
        var access = await PaymentAccess
            .LoadAsync(bookings, parties, command.UserId, Roles.Owner, command.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (access.IsFailure)
            return Result<PaymentSessionResponse>.FromFailure(access);

        var booking = access.Value!.Booking;

        var payment = await paymentRepository.GetByBookingIdAsync(booking.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
            return Result<PaymentSessionResponse>.Failure(PaymentsErrors.NotFound(booking.Id));

        if (booking.Stage is BookingStage.Cancelled or BookingStage.Closed)
            return Result<PaymentSessionResponse>.Failure(PaymentsErrors.Closed);

        var now = timeProvider.GetUtcNow();
        var number = command.InstallmentNumber;

        var payable = payment.EnsurePayable(number, now);
        if (payable.IsFailure)
            return Result<PaymentSessionResponse>.FromFailure(payable);

        var installment = payable.Value!;
        var pixExpiresAt = payment.PixExpiresAt(number, booking.IsBeforeCheckIn, now);

        StripeCheckout? checkout = null;

        if (installment is { IntentOpen: true, StripePaymentIntentId: { } openIntent })
        {
            var prepared = await stripe.PrepareIntentAsync(openIntent, pixExpiresAt, cancellationToken).ConfigureAwait(false);
            if (prepared.IsFailure)
                return Result<PaymentSessionResponse>.FromFailure(prepared);

            switch (prepared.Value!.State)
            {
                case ChargeState.AwaitingPayment:
                    checkout = prepared.Value;
                    break;

                case ChargeState.Processing or ChargeState.Succeeded:
                    return await AlreadyPaidAsync(payment, installment, cancellationToken).ConfigureAwait(false);
            }
        }

        if (checkout is null)
        {
            var created = await stripe
                .CreateIntentAsync(
                    new IntentDraft(
                        installment.Id,
                        payment.Id.Value,
                        payment.BookingId,
                        number,
                        payment.InstallmentCount,
                        installment.PaymentAttempts + 1,
                        installment.AmountCents,
                        pixExpiresAt),
                    cancellationToken)
                .ConfigureAwait(false);

            if (created.IsFailure)
                return Result<PaymentSessionResponse>.FromFailure(created);

            checkout = created.Value!;
            payment.AttachIntent(number, checkout.PaymentIntentId, now);

            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<PaymentSessionResponse>.FromFailure(saved);
        }

        return Result<PaymentSessionResponse>.Success(new PaymentSessionResponse(
            payment.BookingId,
            number,
            payment.InstallmentCount,
            PaymentResponseMapper.Reais(installment.AmountCents),
            checkout.PaymentIntentId,
            checkout.ClientSecret,
            stripe.PublishableKey,
            pixExpiresAt.ToUnixTimeSeconds(),
            PaymentMethods));
    }

    /// <summary>
    /// O Stripe já recebeu o pagamento (o webhook ainda não chegou): relê, reconcilia, grava e
    /// conta ao tutor que a parcela está sendo confirmada ou já foi paga.
    /// </summary>
    private async Task<Result<PaymentSessionResponse>> AlreadyPaidAsync(Payment payment, Installment installment, CancellationToken cancellationToken)
    {
        var effects = new PaymentEffects();

        var refreshed = await reconciler.RefreshAsync(payment, installment, effects, cancellationToken).ConfigureAwait(false);
        if (refreshed.IsFailure)
            return Result<PaymentSessionResponse>.FromFailure(refreshed);

        var reconciled = await reconciler.ReconcileAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        if (reconciled.IsFailure)
            return Result<PaymentSessionResponse>.FromFailure(reconciled);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<PaymentSessionResponse>.FromFailure(saved);

        await publisher.PublishAsync(payment, effects, cancellationToken).ConfigureAwait(false);

        return Result<PaymentSessionResponse>.Failure(installment.IsPaid
            ? PaymentsErrors.InstallmentAlreadyPaid(installment.Number)
            : PaymentsErrors.InstallmentProcessing(installment.Number));
    }
}
