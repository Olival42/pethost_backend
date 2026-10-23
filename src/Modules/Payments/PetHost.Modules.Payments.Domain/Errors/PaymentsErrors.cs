using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Payments.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Payments (pagamentos). Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class PaymentsErrors
{
    /// <summary>Maior página da listagem (§13).</summary>
    public const int MaxPageSize = 100;

    // --- Validação (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error InstallmentCountInvalid =
        Error.Validation("installmentCount", "PAYMENT_INSTALLMENT_COUNT_INVALID", $"Installment count must be between 1 and {Payment.MaxInstallments}.", ("max", Payment.MaxInstallments));

    public static readonly Error InstallmentNumberInvalid =
        Error.Validation("installmentNumber", "PAYMENT_INSTALLMENT_NUMBER_INVALID", $"Installment number must be between 1 and {Payment.MaxInstallments}.", ("max", Payment.MaxInstallments));

    public static readonly Error PageInvalid =
        Error.Validation("page", "PAYMENT_PAGE_INVALID", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", "PAYMENT_PAGE_SIZE_INVALID", $"Page size must be between 1 and {MaxPageSize}.", ("max", MaxPageSize));

    // --- Quem pede ---

    /// <summary>
    /// 404: a reserva não existe ou não é de quem pede (nem o tutor que pediu, nem o anfitrião).
    /// As duas situações respondem igual de propósito (§8).
    /// </summary>
    public static Error BookingNotFound(Guid bookingId) =>
        new("PAYMENT_BOOKING_NOT_FOUND", $"Booking '{bookingId}' was not found.");

    /// <summary>404: a reserva ainda não tem pagamento (o tutor não escolheu as parcelas).</summary>
    public static Error NotFound(Guid bookingId) =>
        new("PAYMENT_NOT_FOUND", $"Booking '{bookingId}' has no payment yet.");

    /// <summary>404: a conta do token não tem perfil de tutor.</summary>
    public static readonly Error OwnerNotFound =
        new("PAYMENT_OWNER_NOT_FOUND", "This account has no owner profile.");

    /// <summary>404: a conta do token não tem perfil de anfitrião.</summary>
    public static readonly Error HostNotFound =
        new("PAYMENT_HOST_NOT_FOUND", "This account has no verified host profile yet.");

    // --- Plano de parcelas ---

    /// <summary>422: só reserva aceita (esperando a 1ª parcela) escolhe as parcelas.</summary>
    public static readonly Error BookingNotAwaitingPayment =
        new("PAYMENT_BOOKING_NOT_AWAITING_PAYMENT", "Only an accepted booking waiting for payment can be paid.");

    /// <summary>422: passou o prazo para pagar a 1ª parcela (24h depois do aceite). A reserva expira.</summary>
    public static readonly Error PaymentWindowClosed =
        new("PAYMENT_WINDOW_CLOSED", "The time to pay the first installment is over; the booking will expire.");

    /// <summary>422: mais parcelas do que o valor permite (cada uma de pelo menos R$ 50,00).</summary>
    public static Error InstallmentCountNotAllowed(int max) =>
        new(
            "PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED",
            max == 1
                ? "This booking can only be paid in full (each installment must be at least R$ 50.00)."
                : $"This booking can be paid in up to {max} installments (each installment must be at least R$ 50.00).");

    /// <summary>422: algo já foi pago (ou está sendo pago): o número de parcelas não muda mais.</summary>
    public static readonly Error PlanLocked =
        new("PAYMENT_PLAN_LOCKED", "The installment plan can no longer change: a payment was already made or is being processed.");

    /// <summary>422: a reserva foi cancelada ou expirou; não há mais o que pagar.</summary>
    public static readonly Error Closed =
        new("PAYMENT_CLOSED", "This booking was cancelled or expired; there is nothing left to pay.");

    // --- Parcela ---

    /// <summary>404: o plano não tem essa parcela.</summary>
    public static Error InstallmentNotFound(int number) =>
        new("PAYMENT_INSTALLMENT_NOT_FOUND", $"Installment {number} does not exist in this payment plan.");

    /// <summary>422: a parcela já foi paga.</summary>
    public static Error InstallmentAlreadyPaid(int number) =>
        new("PAYMENT_INSTALLMENT_ALREADY_PAID", $"Installment {number} is already paid.");

    /// <summary>422: o pagamento da parcela está sendo confirmado (Pix compensando, cartão em análise).</summary>
    public static Error InstallmentProcessing(int number) =>
        new("PAYMENT_INSTALLMENT_PROCESSING", $"The payment of installment {number} is being processed. Wait for the confirmation.");

    /// <summary>422: há uma parcela anterior em aberto; as parcelas são pagas em ordem.</summary>
    public static Error InstallmentOutOfOrder(int earlier) =>
        new("PAYMENT_INSTALLMENT_OUT_OF_ORDER", $"Pay installment {earlier} first; installments are paid in order.");

    // --- Stripe e gravação ---

    /// <summary>503: o Stripe está fora do ar, lento ou sem chave configurada. Tente de novo depois.</summary>
    public static readonly Error StripeUnavailable =
        new("PAYMENT_STRIPE_UNAVAILABLE", "The payment service is unavailable right now. Try again in a few minutes.");

    /// <summary>422: o Stripe recusou o pedido (dado que ele não aceita).</summary>
    public static readonly Error StripeRequestRejected =
        new("PAYMENT_STRIPE_REQUEST_REJECTED", "The payment service rejected the request.");

    /// <summary>409: o pagamento mudou ao mesmo tempo (um aviso do Stripe chegou junto). Leia de novo e tente outra vez.</summary>
    public static readonly Error Conflict =
        new("PAYMENT_CONFLICT", "This payment was changed at the same time. Reload it and try again.");

    // --- Validação: identificadores e campos obrigatórios de comandos internos e de rota ---

    public static readonly Error BookingIdRequired =
        Error.Validation("bookingId", "PAYMENT_BOOKING_ID_REQUIRED", "Booking id is required.");

    public static readonly Error StripeObjectIdRequired =
        Error.Validation("stripeObjectId", "PAYMENT_STRIPE_OBJECT_ID_REQUIRED", "Stripe object id is required.");
}
