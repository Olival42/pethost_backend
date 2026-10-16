using FluentAssertions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Domain.UnitTests.Payments;

/// <summary>A cobrança relida do Stripe, a reconciliação com a reserva (reembolso, repasse, contestação), o atraso e os lembretes.</summary>
public sealed class PaymentLifecycleTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow;

    // ----- releitura da cobrança -----

    [Fact]
    public void ApplyCharge_Should_MarkPaidAndActivate_When_StripeConfirmsTheFirstOfThree()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);

        // Act
        var change = payment.ApplyCharge(1, PaymentBuilder.Succeeded(1, PaymentMethod.Pix), Now.AddMinutes(2));

        // Assert
        change.BecamePaid.Should().BeTrue();
        var installment = payment.FindInstallment(1)!;
        installment.Status.Should().Be(InstallmentStatus.Paid);
        installment.Method.Should().Be(PaymentMethod.Pix);
        installment.StripeChargeId.Should().Be(PaymentBuilder.ChargeId(1));
        installment.PaidAt.Should().Be(Now.AddMinutes(2));
        installment.IntentOpen.Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.Active);
        payment.PaidCents.Should().Be(10_000);
    }

    [Fact]
    public void ApplyCharge_Should_MarkThePaymentPaid_When_TheOnlyInstallmentIsPaid()
    {
        // Act
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.SettledAt.Should().BeNull("the payout is still held until check-out");
    }

    [Fact]
    public void ApplyCharge_Should_ChangeNothing_When_TheSameSnapshotArrivesTwice()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var paidAt = payment.FindInstallment(1)!.PaidAt;

        // Act
        var change = payment.ApplyCharge(1, PaymentBuilder.Succeeded(1), Now.AddHours(5));

        // Assert
        change.BecamePaid.Should().BeFalse();
        payment.FindInstallment(1)!.PaidAt.Should().Be(paidAt);
    }

    [Fact]
    public void ApplyCharge_Should_ReturnToPendingWithTheFailure_When_ProcessingPaymentFails()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.Processing, PaymentMethod.Card), Now);

        // Act
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.AwaitingPayment, PaymentMethod.Card, FailureCode: "card_declined"), Now.AddMinutes(1));

        // Assert
        var installment = payment.FindInstallment(1)!;
        installment.Status.Should().Be(InstallmentStatus.Pending);
        installment.FailureCode.Should().Be("card_declined");
        installment.IntentOpen.Should().BeTrue();
    }

    [Fact]
    public void ApplyCharge_Should_CloseTheIntent_When_StripeCanceledIt()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);

        // Act
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.Canceled), Now.AddMinutes(1));

        // Assert
        payment.FindInstallment(1)!.IntentOpen.Should().BeFalse();
        payment.FindInstallment(1)!.Status.Should().Be(InstallmentStatus.Pending);
    }

    [Fact]
    public void ApplyCharge_Should_ReportDisputeOpenedThenClosed_When_OwnerContestsTheCard()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var opened = payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Open) }, Now.AddDays(1));
        var closed = payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Won) }, Now.AddDays(20));

        // Assert
        opened.DisputeOpened.Should().BeTrue();
        opened.DisputeClosed.Should().BeFalse();
        closed.DisputeClosed.Should().BeTrue();
        closed.Dispute.Should().Be(DisputeStatus.Won);
        payment.FindInstallment(1)!.DisputedAt.Should().Be(Now.AddDays(1));
        payment.FindInstallment(1)!.DisputeClosedAt.Should().Be(Now.AddDays(20));
    }

    [Fact]
    public void ApplyCharge_Should_ReportRefundFailedOnce_When_StripeCannotRefund()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var failed = PaymentBuilder.Succeeded(1) with { Refund = new RefundSnapshot("re_1", 10_000, RefundStatus.Failed) };

        // Act
        var first = payment.ApplyCharge(1, failed, Now.AddDays(1));
        var second = payment.ApplyCharge(1, failed, Now.AddDays(2));

        // Assert
        first.RefundFailed.Should().BeTrue();
        second.RefundFailed.Should().BeFalse();
    }

    // ----- reconciliação: check-out e repasse -----

    [Fact]
    public void Reconcile_Should_DoNothing_When_BookingIsConfirmedBeforeCheckOut()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var actions = payment.Reconcile(BookingStage.Confirmed, null, Now.AddDays(1));

        // Assert
        actions.Should().BeEmpty();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Held);
    }

    [Fact]
    public void Reconcile_Should_TransferNinetyPercentOfEachPaidInstallment_When_StayIsCompleted()
    {
        // Arrange — 2 de 3 parcelas pagas.
        var payment = new PaymentBuilder().WithAmount(30_001).BuildPaid(2);

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(10));

        // Assert
        actions.Should().Equal(
            new PaymentAction(PaymentActionKind.Transfer, 1, PaymentBuilder.ChargeId(1), 9_000, 1_001),
            new PaymentAction(PaymentActionKind.Transfer, 2, PaymentBuilder.ChargeId(2), 9_000, 1_000));
    }

    [Fact]
    public void Reconcile_Should_NotRepeatTheTransfer_When_ItWasRecorded()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 27_000, 3_000, Now.AddDays(10));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(11));

        // Assert
        actions.Should().BeEmpty();
        var installment = payment.FindInstallment(1)!;
        installment.PayoutStatus.Should().Be(PayoutStatus.Transferred);
        installment.HostPayoutCents.Should().Be(27_000);
        installment.PlatformFeeCents.Should().Be(3_000);
        payment.SettledAt.Should().Be(Now.AddDays(10), "everything was settled when the transfer was recorded");
    }

    [Fact]
    public void Reconcile_Should_TransferALaterInstallment_When_ItIsPaidAfterCheckOut()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 9_000, 1_000, Now.AddDays(10));
        PaymentBuilder.Pay(payment, 2, Now.AddDays(30));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(30));

        // Assert
        actions.Should().Equal(new PaymentAction(PaymentActionKind.Transfer, 2, PaymentBuilder.ChargeId(2), 9_000, 1_000));
    }

    [Fact]
    public void Reconcile_Should_WaitForTheCharge_When_PaidInstallmentHasNoChargeIdYet()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.Succeeded, PaymentMethod.Card), Now);

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(10));

        // Assert
        actions.Should().BeEmpty();
    }

    [Fact]
    public void RecordTransferFailed_Should_KeepThePayoutHeldWithTheReason()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);

        // Act
        payment.RecordTransferFailed(1, "NO_CONNECTED_ACCOUNT", Now.AddDays(10));

        // Assert
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Held);
        payment.FindInstallment(1)!.PayoutFailureCode.Should().Be("NO_CONNECTED_ACCOUNT");
        payment.Reconcile(BookingStage.Completed, null, Now.AddDays(11)).Should().ContainSingle(a => a.Kind == PaymentActionKind.Transfer);
    }

    // ----- reconciliação: cancelamento e reembolso -----

    [Fact]
    public void Reconcile_Should_CancelOpenInstallmentsAndRefundEverything_When_HostCancels()
    {
        // Arrange — 1ª paga; 2ª com cobrança aberta (QR code do Pix gerado).
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), Now.AddDays(1));

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, 100, Now.AddDays(2));

        // Assert
        payment.Status.Should().Be(PaymentStatus.Cancelled);
        payment.RefundPercentage.Should().Be(100);
        payment.CancelledAt.Should().Be(Now.AddDays(2));
        payment.Installments.Where(i => i.Number > 1).Should().OnlyContain(i => i.Status == InstallmentStatus.Cancelled && i.PayoutStatus == PayoutStatus.Cancelled);
        actions.Should().Equal(
            new PaymentAction(PaymentActionKind.Refund, 1, PaymentBuilder.IntentId(1), 10_000),
            new PaymentAction(PaymentActionKind.CancelIntent, 2, PaymentBuilder.IntentId(2)));
    }

    [Fact]
    public void Reconcile_Should_CancelThePayoutAndSettle_When_FullRefundSucceeded()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.Reconcile(BookingStage.Cancelled, 100, Now.AddDays(2));
        payment.RecordRefund(1, "re_1", 30_000, RefundStatus.Succeeded, Now.AddDays(2));

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, 100, Now.AddDays(2));

        // Assert
        actions.Should().BeEmpty();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Cancelled);
        payment.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public void Reconcile_Should_RefundHalfThenTransferTheHostShareOfTheRest_When_OwnerCancelsLate()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);

        // Act
        var refund = payment.Reconcile(BookingStage.Cancelled, 50, Now.AddDays(8));
        payment.RecordRefund(1, "re_1", refund[0].AmountCents, RefundStatus.Pending, Now.AddDays(8));
        var transfer = payment.Reconcile(BookingStage.Cancelled, 50, Now.AddDays(8));

        // Assert
        refund.Should().Equal(new PaymentAction(PaymentActionKind.Refund, 1, PaymentBuilder.IntentId(1), 15_000));
        transfer.Should().Equal(new PaymentAction(PaymentActionKind.Transfer, 1, PaymentBuilder.ChargeId(1), 13_500, 1_500));
        payment.SettledAt.Should().BeNull("the Pix refund is still on its way");
    }

    [Fact]
    public void Reconcile_Should_ApplyTheRefundPercentageToWhatWasPaid_When_PaidInInstallments()
    {
        // Arrange — 2 de 3 parcelas pagas, cancelamento tardio do tutor.
        var payment = new PaymentBuilder().BuildPaid(2);

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, 50, Now.AddDays(8));

        // Assert
        actions.Where(a => a.Kind == PaymentActionKind.Refund).Select(a => a.AmountCents).Should().Equal(5_000, 5_000);
        payment.FindInstallment(3)!.Status.Should().Be(InstallmentStatus.Cancelled);
    }

    [Fact]
    public void Reconcile_Should_RefundEverything_When_BookingExpiredWhileTheOwnerWasPaying()
    {
        // Arrange — o Stripe confirmou depois que a reserva expirou.
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var actions = payment.Reconcile(BookingStage.Closed, null, Now.AddDays(1));

        // Assert
        payment.RefundPercentage.Should().Be(100);
        actions.Should().ContainSingle().Which.Should().Be(new PaymentAction(PaymentActionKind.Refund, 1, PaymentBuilder.IntentId(1), 10_000));
    }

    [Fact]
    public void Reconcile_Should_RefundAPaymentThatArrivedAfterCancellation_When_TheIntentSucceedsLater()
    {
        // Arrange — cancelada sem nada pago; depois o Pix compensa.
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.Reconcile(BookingStage.Cancelled, null, Now.AddHours(1));
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1, PaymentMethod.Pix), Now.AddHours(2));

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, null, Now.AddHours(2));

        // Assert
        actions.Should().Equal(new PaymentAction(PaymentActionKind.Refund, 1, PaymentBuilder.IntentId(1), 30_000));
    }

    [Fact]
    public void Reconcile_Should_NotTransfer_When_TheRefundFailed()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.Reconcile(BookingStage.Cancelled, 50, Now.AddDays(8));
        payment.RecordRefund(1, "re_1", 15_000, RefundStatus.Failed, Now.AddDays(8));

        // Act
        var actions = payment.Reconcile(BookingStage.Cancelled, 50, Now.AddDays(9));

        // Assert
        actions.Should().BeEmpty();
        payment.SettledAt.Should().BeNull();
    }

    [Fact]
    public void RecordIntentCancelled_Should_SettleACancelledPlanWithNothingPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.Reconcile(BookingStage.Closed, null, Now.AddHours(25));

        // Act
        payment.RecordIntentCancelled(1, Now.AddHours(25));

        // Assert
        payment.SettledAt.Should().Be(Now.AddHours(25));
        payment.Reconcile(BookingStage.Closed, null, Now.AddHours(26)).Should().BeEmpty();
    }

    // ----- reconciliação: contestação -----

    [Fact]
    public void Reconcile_Should_HoldEverything_When_DisputeIsOpen()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Open) }, Now.AddDays(5));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(10));

        // Assert
        actions.Should().BeEmpty();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Held);
    }

    [Fact]
    public void Reconcile_Should_TransferNormally_When_DisputeWasWon()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Won) }, Now.AddDays(20));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(20));

        // Assert
        actions.Should().ContainSingle(a => a.Kind == PaymentActionKind.Transfer);
    }

    [Fact]
    public void Reconcile_Should_CancelThePayout_When_DisputeIsLostBeforeTheTransfer()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Lost) }, Now.AddDays(20));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(20));

        // Assert
        actions.Should().BeEmpty();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Cancelled);
        payment.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public void Reconcile_Should_ReverseTheHostTransfer_When_DisputeIsLostAfterThePayout()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 27_000, 3_000, Now.AddDays(10));
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Lost) }, Now.AddDays(40));

        // Act
        var actions = payment.Reconcile(BookingStage.Completed, null, Now.AddDays(40));
        payment.RecordReversal(1, "trr_1", 27_000, Now.AddDays(40));

        // Assert
        actions.Should().Equal(new PaymentAction(PaymentActionKind.ReverseTransfer, 1, "tr_1", 27_000));
        var installment = payment.FindInstallment(1)!;
        installment.PayoutStatus.Should().Be(PayoutStatus.Reversed);
        installment.ReversedCents.Should().Be(27_000);
        payment.Reconcile(BookingStage.Completed, null, Now.AddDays(41)).Should().BeEmpty();
    }

    // ----- atraso e lembretes -----

    [Fact]
    public void CheckOverdue_Should_AskToCancelTheBooking_When_InstallmentIsMissedBeforeCheckIn()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var dueAt = payment.FindInstallment(2)!.DueAt;

        // Act
        var check = payment.CheckOverdue(BookingStage.Confirmed, dueAt + Payment.OverdueGrace);

        // Assert
        check.MissedBeforeCheckIn.Should().BeTrue();
        payment.HasOverdueInstallment.Should().BeFalse();
    }

    [Fact]
    public void CheckOverdue_Should_WaitTheGrace_When_InstallmentJustExpired()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var dueAt = payment.FindInstallment(2)!.DueAt;

        // Act
        var check = payment.CheckOverdue(BookingStage.Confirmed, dueAt.AddHours(23));

        // Assert
        check.Should().Be(OverdueCheck.None);
    }

    [Fact]
    public void CheckOverdue_Should_MarkOverdueButKeepItPayable_When_StayAlreadyHappened()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var at = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;

        // Act
        var check = payment.CheckOverdue(BookingStage.Completed, at);

        // Assert
        check.MissedBeforeCheckIn.Should().BeFalse();
        check.BecameOverdue.Should().Equal(2);
        payment.HasOverdueInstallment.Should().BeTrue();
        payment.FindInstallment(2)!.OverdueSince.Should().Be(at);
        payment.EnsurePayable(2, at.AddDays(1)).IsSuccess.Should().BeTrue();
        payment.CheckOverdue(BookingStage.Completed, at.AddDays(1)).BecameOverdue.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCharge_Should_ClearTheOverdue_When_TheOverdueInstallmentIsPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(2).BuildPaid(1);
        var at = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        payment.CheckOverdue(BookingStage.Completed, at);

        // Act
        PaymentBuilder.Pay(payment, 2, at.AddDays(2));

        // Assert
        payment.HasOverdueInstallment.Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void CheckOverdue_Should_IgnoreTheFirstInstallment_When_ItIsLate()
    {
        // Arrange — a 1ª vencida é a expiração da reserva, não atraso.
        var payment = new PaymentBuilder().Build();

        // Act
        var check = payment.CheckOverdue(BookingStage.AwaitingPayment, PaymentBuilder.DefaultDeadline.AddDays(2));

        // Assert
        check.Should().Be(OverdueCheck.None);
    }

    [Fact]
    public void TakeReminders_Should_RemindOnce_When_InstallmentIsDueWithinThreeDays()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var dueAt = payment.FindInstallment(2)!.DueAt;

        // Act
        var first = payment.TakeReminders(dueAt.AddDays(-2));
        var second = payment.TakeReminders(dueAt.AddDays(-1));

        // Assert
        first.Select(i => i.Number).Should().Equal(2);
        second.Should().BeEmpty();
        payment.FindInstallment(2)!.ReminderSentAt.Should().Be(dueAt.AddDays(-2));
    }

    [Fact]
    public void TakeReminders_Should_RemindNothing_When_DueDateIsStillFar()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var reminders = payment.TakeReminders(Now.AddDays(5));

        // Assert
        reminders.Should().BeEmpty();
    }

    // ----- valores no banco e no JSON -----

    [Fact]
    public void Values_Should_RoundTripEveryEnum()
    {
        // Assert
        Enum.GetValues<PaymentStatus>().Should().OnlyContain(v => PaymentStatusValues.FromWire(PaymentStatusValues.ToWire(v)) == v);
        Enum.GetValues<InstallmentStatus>().Should().OnlyContain(v => InstallmentStatusValues.FromWire(InstallmentStatusValues.ToWire(v)) == v);
        Enum.GetValues<PaymentMethod>().Should().OnlyContain(v => PaymentMethodValues.FromWire(PaymentMethodValues.ToWire(v)) == v);
        Enum.GetValues<RefundStatus>().Should().OnlyContain(v => RefundStatusValues.FromWire(RefundStatusValues.ToWire(v)) == v);
        Enum.GetValues<PayoutStatus>().Should().OnlyContain(v => PayoutStatusValues.FromWire(PayoutStatusValues.ToWire(v)) == v);
        Enum.GetValues<DisputeStatus>().Should().OnlyContain(v => DisputeStatusValues.FromWire(DisputeStatusValues.ToWire(v)) == v);
    }

    [Fact]
    public void Values_Should_Throw_When_DatabaseHasAnUnknownValue()
    {
        // Act
        var act = () => PaymentStatusValues.FromWire("refunded");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PaymentClock_Should_UseBrasiliaDates()
    {
        // Arrange — 23h do dia 19 em Brasília = 02h UTC do dia 20.
        var lateNight = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);

        // Assert
        PaymentClock.Today(lateNight).Should().Be(new DateOnly(2026, 10, 19));
        PaymentClock.EndOf(new DateOnly(2026, 10, 19)).Should().Be(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero));
    }
}
