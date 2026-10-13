using Moq;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;

namespace PetHost.Modules.Payments.Application.UnitTests.Payments;

/// <summary>Contas de exemplo e os dublês das portas do módulo.</summary>
internal static class PaymentsTestData
{
    /// <summary>Camila: a tutora (perfil <see cref="BookingBuilder.OwnerId"/>).</summary>
    public static readonly Guid OwnerUserId = Guid.CreateVersion7();

    /// <summary>Dona Cida: a anfitriã (perfil <see cref="BookingBuilder.HostId"/>).</summary>
    public static readonly Guid HostUserId = Guid.CreateVersion7();

    /// <summary>Outra tutora, com perfil, sem nada a ver com a reserva.</summary>
    public static readonly Guid OtherOwnerUserId = Guid.CreateVersion7();

    /// <summary>Conta sem perfil nenhum.</summary>
    public static readonly Guid NoProfileUserId = Guid.CreateVersion7();

    public const string HostAccountId = "acct_test_cida";

    public static readonly PartyContact OwnerContact = new("Camila Souza", "camila@pethost.test");
    public static readonly PartyContact HostContact = new("Dona Cida", "cida@pethost.test");

    /// <summary>A reserva do <see cref="PaymentBuilder"/> no estágio <paramref name="stage"/>.</summary>
    public static PaymentBooking Booking(BookingStage stage = BookingStage.AwaitingPayment, int? refundPercentage = null, long amountCents = PaymentBuilder.DefaultAmountCents) => new(
        PaymentBuilder.BookingId,
        BookingBuilder.OwnerId,
        BookingBuilder.HostId,
        stage,
        amountCents,
        BookingBuilder.DefaultCheckIn,
        BookingBuilder.DefaultCheckOut,
        stage == BookingStage.AwaitingPayment ? PaymentBuilder.DefaultDeadline : null,
        refundPercentage);

    /// <summary>Reservas que devolvem, a cada leitura, o próximo estágio da fila (o último se repete).</summary>
    public static Mock<IPaymentBookings> Bookings(params PaymentBooking?[] sequence)
    {
        var bookings = new Mock<IPaymentBookings>(MockBehavior.Strict);
        var reads = new Queue<PaymentBooking?>(sequence.Length == 0 ? [Booking()] : sequence);
        var last = reads.Last();

        bookings.Setup(b => b.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => reads.Count > 1 ? reads.Dequeue() : last);
        bookings.Setup(b => b.ConfirmPaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(BookingPaymentOutcome.Done);
        bookings.Setup(b => b.CancelUnpaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(BookingPaymentOutcome.Done);

        return bookings;
    }

    public static Mock<IPaymentParties> Parties(string? hostAccount = HostAccountId)
    {
        var parties = new Mock<IPaymentParties>(MockBehavior.Strict);

        parties.Setup(p => p.FindOwnerIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        parties.Setup(p => p.FindHostIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        parties.Setup(p => p.FindOwnerIdAsync(OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(BookingBuilder.OwnerId);
        parties.Setup(p => p.FindOwnerIdAsync(OtherOwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.CreateVersion7());
        parties.Setup(p => p.FindHostIdAsync(HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(BookingBuilder.HostId);
        parties.Setup(p => p.GetOwnerContactAsync(BookingBuilder.OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(OwnerContact);
        parties.Setup(p => p.GetHostContactAsync(BookingBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(HostContact);
        parties.Setup(p => p.GetHostPayoutAccountAsync(BookingBuilder.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(hostAccount);

        return parties;
    }

    /// <summary>
    /// Stripe que aceita tudo: cria cobranças <c>pi_new_{n}</c>, devolve reembolsos concluídos,
    /// transfere (<c>tr_{n}</c>) e estorna (<c>trr_{n}</c>). Cada teste troca o que precisa.
    /// </summary>
    public static Mock<IStripePayments> Stripe()
    {
        var stripe = new Mock<IStripePayments>(MockBehavior.Strict);

        stripe.SetupGet(s => s.PublishableKey).Returns("pk_test_pethost");
        stripe.Setup(s => s.CreateIntentAsync(It.IsAny<IntentDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntentDraft d, CancellationToken _) => Result<StripeCheckout>.Success(new StripeCheckout($"pi_new_{d.InstallmentNumber}", $"pi_new_{d.InstallmentNumber}_secret", ChargeState.AwaitingPayment)));
        stripe.Setup(s => s.CancelIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        stripe.Setup(s => s.RefundAsync(It.IsAny<RefundDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefundDraft d, CancellationToken _) => Result<RefundSnapshot>.Success(new RefundSnapshot($"re_{d.InstallmentNumber}", d.AmountCents, RefundStatus.Succeeded)));
        stripe.Setup(s => s.TransferAsync(It.IsAny<TransferDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransferDraft d, CancellationToken _) => Result<string>.Success($"tr_{d.InstallmentNumber}"));
        stripe.Setup(s => s.ReverseTransferAsync(It.IsAny<ReversalDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReversalDraft d, CancellationToken _) => Result<string>.Success($"trr_{d.InstallmentNumber}"));

        return stripe;
    }

    public static Mock<IPaymentsUnitOfWork> UnitOfWork(Result? saveResult = null)
    {
        var unitOfWork = new Mock<IPaymentsUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saveResult ?? Result.Success());
        return unitOfWork;
    }

    public static Mock<IPaymentRepository> Repository(Payment? payment = null)
    {
        var repository = new Mock<IPaymentRepository>();

        repository.Setup(r => r.GetByBookingIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid bookingId, CancellationToken _) => payment?.BookingId == bookingId ? payment : null);
        repository.Setup(r => r.GetByIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string intent, CancellationToken _) => payment?.FindByIntent(intent) is null ? null : payment);

        return repository;
    }

    public static PaymentEffectsPublisher Publisher(Mock<IPaymentNotifier>? notifier = null, Mock<IAuditTrail>? auditTrail = null) =>
        new(Parties().Object, (notifier ?? new Mock<IPaymentNotifier>()).Object, (auditTrail ?? new Mock<IAuditTrail>()).Object);
}
