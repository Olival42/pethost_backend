using FluentAssertions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Domain.UnitTests.Payments;

/// <summary>
/// Multa (2%) e juros (1% ao mês, por dia) da parcela paga com atraso: o cálculo, o valor que a
/// cobrança pede, o repasse e o reembolso sobre o que foi pago, e o lembrete semanal do atraso.
/// </summary>
/// <remarks>
/// No <see cref="PaymentBuilder"/> padrão (3x de R$ 100,00, plano escolhido em 11/10), a 2ª parcela
/// vence em 10/11 (fim do dia em Brasília) e a tolerância acaba à meia-noite de 12/11.
/// </remarks>
public sealed class PaymentLateFeeTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow;

    private static DateTimeOffset GraceEnd(Payment payment, int number = 2) => payment.FindInstallment(number)!.DueAt + Payment.OverdueGrace;

    // ----- cálculo -----

    [Fact]
    public void LateFeeFor_Should_ChargeNothing_When_PaidWithinTheGrace()
    {
        // Arrange
        var dueAt = new PaymentBuilder().Build().FindInstallment(2)!.DueAt;

        // Act
        var fee = Payment.LateFeeFor(10_000, dueAt, dueAt + Payment.OverdueGrace - TimeSpan.FromMinutes(1));

        // Assert
        fee.Should().Be(LateFee.None);
    }

    [Fact]
    public void LateFeeFor_Should_ChargeTheFineAndTwoDaysOfInterest_When_PaidRightAfterTheGrace()
    {
        // Arrange
        var dueAt = new PaymentBuilder().Build().FindInstallment(2)!.DueAt;

        // Act
        var fee = Payment.LateFeeFor(10_000, dueAt, dueAt + Payment.OverdueGrace);

        // Assert
        fee.DaysLate.Should().Be(2, "due on 10/11, paid on 12/11");
        fee.FineCents.Should().Be(200, "2% of R$ 100,00");
        fee.InterestCents.Should().Be(7, "1% a month for 2 days = R$ 0,0666..., rounded");
        fee.TotalCents.Should().Be(207);
    }

    [Fact]
    public void LateFeeFor_Should_ChargeOnePercentOfInterest_When_ThirtyDaysLate()
    {
        // Arrange
        var dueAt = new PaymentBuilder().Build().FindInstallment(2)!.DueAt;

        // Act
        var fee = Payment.LateFeeFor(10_000, dueAt, dueAt.AddDays(29).AddHours(12));

        // Assert
        fee.DaysLate.Should().Be(30);
        fee.InterestCents.Should().Be(100);
        fee.TotalCents.Should().Be(300);
    }

    [Fact]
    public void LateFeeDue_Should_ChargeTheOverdueInstallment_When_ItIsStillOpen()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var at = GraceEnd(payment);
        payment.CheckOverdue(BookingStage.Completed, at);

        // Act
        var fee = payment.LateFeeDue(2, at);

        // Assert
        fee.TotalCents.Should().Be(207);
        payment.AmountDue(2, at).Should().Be(10_207);
    }

    [Fact]
    public void LateFeeDue_Should_ChargeNothing_When_TheInstallmentIsTheFirstOrAlreadyPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(2);
        var muchLater = Now.AddDays(120);

        // Act / Assert
        payment.LateFeeDue(1, muchLater).Should().Be(LateFee.None, "the first installment never runs late: the booking expires");
        payment.LateFeeDue(2, muchLater).Should().Be(LateFee.None, "a paid installment does not change");
        payment.LateFeeDue(3, muchLater).TotalCents.Should().BeGreaterThan(0);
    }

    // ----- cobrança -----

    [Fact]
    public void RecordCharge_Should_KeepTheLateFee_When_TheChargeAsksForMoreThanTheInstallment()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), Now);

        // Act
        payment.RecordCharge(2, 10_207, Now);

        // Assert
        var installment = payment.FindInstallment(2)!;
        installment.LateFeeCents.Should().Be(207);
        installment.ChargedCents.Should().Be(10_207);
        installment.AmountCents.Should().Be(10_000);
    }

    [Fact]
    public void ApplyCharge_Should_KeepWhatTheOwnerPaid_When_StripeConfirmsALateInstallment()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var at = GraceEnd(payment).AddDays(3);
        payment.CheckOverdue(BookingStage.Completed, at);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), at);

        // Act
        payment.ApplyCharge(2, PaymentBuilder.Succeeded(2) with { AmountCents = 10_217 }, at);
        payment.ApplyCharge(2, PaymentBuilder.Succeeded(2) with { AmountCents = 99_999 }, at.AddDays(1));

        // Assert
        var installment = payment.FindInstallment(2)!;
        installment.Status.Should().Be(InstallmentStatus.Paid);
        installment.LateFeeCents.Should().Be(217, "the amount paid does not change after it is paid");
        payment.PaidCents.Should().Be(20_217);
        payment.HasOverdueInstallment.Should().BeFalse();
    }

    // ----- repasse e reembolso -----

    [Fact]
    public void Reconcile_Should_TransferNinetyPercentOfWhatWasPaid_When_TheLateInstallmentIsPaidAfterCheckOut()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), Now);
        payment.ApplyCharge(2, PaymentBuilder.Succeeded(2) with { AmountCents = 10_207 }, Now);

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now);

        // Assert
        actions.Should().ContainSingle(a => a.Kind == PaymentActionKind.Transfer && a.InstallmentNumber == 2)
            .Which.Should().Match<PaymentAction>(a => a.AmountCents == 9_186 && a.PlatformFeeCents == 1_021);
    }

    [Fact]
    public void Reconcile_Should_RefundWhatWasPaid_When_TheBookingIsCancelledWithFullRefund()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { AmountCents = 30_600 }, Now);

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, 100, Now);

        // Assert
        actions.Should().ContainSingle(a => a.Kind == PaymentActionKind.Refund).Which.AmountCents.Should().Be(30_600);
    }

    // ----- lembrete do atraso -----

    [Fact]
    public void TakeOverdueReminders_Should_RemindEverySevenDays_When_TheInstallmentStaysOverdue()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var overdueAt = GraceEnd(payment);
        payment.CheckOverdue(BookingStage.Completed, overdueAt);

        // Act / Assert
        payment.TakeOverdueReminders(overdueAt.AddDays(6)).Should().BeEmpty("the overdue notice went out less than a week ago");

        payment.TakeOverdueReminders(overdueAt.AddDays(7)).Select(i => i.Number).Should().Equal(2);
        payment.FindInstallment(2)!.OverdueRemindedAt.Should().Be(overdueAt.AddDays(7));

        payment.TakeOverdueReminders(overdueAt.AddDays(10)).Should().BeEmpty("already reminded this week");
        payment.TakeOverdueReminders(overdueAt.AddDays(14)).Select(i => i.Number).Should().Equal(2);
    }

    [Fact]
    public void TakeOverdueReminders_Should_Stop_When_TheOverdueInstallmentIsPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var overdueAt = GraceEnd(payment);
        payment.CheckOverdue(BookingStage.Completed, overdueAt);
        PaymentBuilder.Pay(payment, 2, overdueAt.AddDays(1));

        // Act
        var reminders = payment.TakeOverdueReminders(overdueAt.AddDays(7));

        // Assert
        reminders.Should().BeEmpty();
    }
}
