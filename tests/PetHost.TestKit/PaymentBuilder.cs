using PetHost.Modules.Payments.Domain.Payments;

namespace PetHost.TestKit;

/// <summary>
/// Monta um pagamento para teste: por padrão, a reserva da Camila no cantinho da Dona Cida
/// (R$ 300,00), aceita em <see cref="BookingBuilder.DefaultNow"/> com 24h para pagar, em 3 parcelas
/// de R$ 100,00. Cada <c>With...</c> troca uma parte; o teste declara só o que importa.
/// </summary>
public sealed class PaymentBuilder
{
    public static readonly DateTimeOffset DefaultNow = BookingBuilder.DefaultNow;

    /// <summary>Prazo da 1ª parcela: 24h depois do aceite.</summary>
    public static readonly DateTimeOffset DefaultDeadline = DefaultNow.AddHours(24);

    public static readonly Guid BookingId = Guid.Parse("0199b000-0000-7000-8000-000000000001");

    public const long DefaultAmountCents = 30_000;

    private long _amountCents = DefaultAmountCents;
    private int _installments = 3;
    private DateTimeOffset _deadline = DefaultDeadline;
    private Guid _bookingId = BookingId;

    public PaymentBuilder WithAmount(long cents)
    {
        _amountCents = cents;
        return this;
    }

    public PaymentBuilder WithInstallments(int count)
    {
        _installments = count;
        return this;
    }

    public PaymentBuilder WithDeadline(DateTimeOffset deadline)
    {
        _deadline = deadline;
        return this;
    }

    public PaymentBuilder ForBooking(Guid bookingId)
    {
        _bookingId = bookingId;
        return this;
    }

    /// <summary>A reserva aceita, como o pagamento a recebe.</summary>
    public PayableBooking Booking() => new(_bookingId, BookingBuilder.OwnerId, BookingBuilder.HostId, _amountCents, _deadline);

    /// <summary>O plano escolhido, nada pago. Falha do domínio vira exceção: o builder só monta caso válido.</summary>
    public Payment Build()
    {
        var payment = Payment.Create(Booking(), _installments, DefaultNow);

        return payment.IsSuccess
            ? payment.Value!
            : throw new InvalidOperationException($"Invalid test payment: {payment.FirstError!.Message}");
    }

    /// <summary>O plano com as primeiras <paramref name="paidCount"/> parcelas pagas no cartão (cobranças <c>pi_test_{n}</c>, <c>ch_test_{n}</c>).</summary>
    public Payment BuildPaid(int paidCount = 1)
    {
        var payment = Build();

        for (var number = 1; number <= paidCount; number++)
            Pay(payment, number, DefaultNow.AddHours(number));

        return payment;
    }

    /// <summary>Abre a cobrança da parcela e aplica o pagamento confirmado pelo Stripe.</summary>
    public static void Pay(Payment payment, int number, DateTimeOffset at, PaymentMethod method = PaymentMethod.Card)
    {
        ArgumentNullException.ThrowIfNull(payment);

        payment.AttachIntent(number, IntentId(number), at);
        payment.ApplyCharge(number, Succeeded(number, method), at);
    }

    public static string IntentId(int number) => $"pi_test_{number}";

    public static string ChargeId(int number) => $"ch_test_{number}";

    /// <summary>A cobrança da parcela paga e confirmada.</summary>
    public static ChargeSnapshot Succeeded(int number, PaymentMethod method = PaymentMethod.Card) =>
        new(ChargeState.Succeeded, method, ChargeId(number));
}
