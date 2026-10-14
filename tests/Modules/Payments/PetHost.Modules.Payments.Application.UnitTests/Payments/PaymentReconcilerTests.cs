using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Application.UnitTests.Payments;

public sealed class PaymentReconcilerTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow.AddHours(3);

    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private readonly PaymentEffects _effects = new();
    private Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings();
    private Mock<IPaymentParties> _parties = PaymentsTestData.Parties();
    private DateTimeOffset _now = Now;

    private PaymentReconciler Sut() => new(_stripe.Object, _bookings.Object, _parties.Object, new FakeTimeProvider(_now));

    [Fact]
    public async Task ReconcileAsync_Should_ConfirmTheBooking_When_FirstInstallmentWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(), PaymentsTestData.Booking(BookingStage.Confirmed));

        // Act
        var result = await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _bookings.Verify(b => b.ConfirmPaidAsync(PaymentBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_Should_NotConfirm_When_NothingWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _bookings.Verify(b => b.ConfirmPaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_Should_RefundEverything_When_BookingExpiredWhileTheOwnerPaid()
    {
        // Arrange — confirmar não se aplica mais; a reserva relida está expirada.
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(), PaymentsTestData.Booking(BookingStage.Closed));
        _bookings.Setup(b => b.ConfirmPaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(BookingPaymentOutcome.NotApplicable);

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _stripe.Verify(s => s.RefundAsync(It.Is<RefundDraft>(d => d.AmountCents == 30_000 && d.PaymentIntentId == PaymentBuilder.IntentId(1)), It.IsAny<CancellationToken>()), Times.Once);
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Cancelled);
        _effects.Items.Select(e => e.Kind).Should().Contain(PaymentEffectKind.RefundCreated);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnConflict_When_TheBookingChangedAtTheSameTime()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        _bookings.Setup(b => b.ConfirmPaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(BookingPaymentOutcome.Conflict);

        // Act
        var result = await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Conflict);
    }

    [Fact]
    public async Task ReconcileAsync_Should_CancelTheBookingAndRefund_When_InstallmentIsMissedBeforeCheckIn()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        _now = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed), PaymentsTestData.Booking(BookingStage.Cancelled, refundPercentage: 100));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _bookings.Verify(b => b.CancelUnpaidAsync(PaymentBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        payment.Status.Should().Be(PaymentStatus.Cancelled);
        _stripe.Verify(s => s.RefundAsync(It.Is<RefundDraft>(d => d.InstallmentNumber == 1 && d.AmountCents == 10_000), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReturnConflict_When_CancellingTheUnpaidBookingConflicts()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        _now = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed));
        _bookings.Setup(b => b.CancelUnpaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(BookingPaymentOutcome.Conflict);

        // Act
        var result = await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Conflict);
    }

    [Fact]
    public async Task ReconcileAsync_Should_MarkOverdueAndRecord_When_InstallmentIsMissedAfterTheStay()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 9_000, 1_000, Now);
        _now = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        payment.HasOverdueInstallment.Should().BeTrue();
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.InstallmentOverdue && e.InstallmentNumber == 2)
            .Which.AmountCents.Should().Be(10_207, "the notice shows the amount with the 2% fine and 2 days of interest");
        _bookings.Verify(b => b.CancelUnpaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_Should_RemindWithTheUpdatedAmount_When_InstallmentIsStillOverdueAWeekLater()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 9_000, 1_000, Now);
        var overdueAt = payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        payment.CheckOverdue(BookingStage.Completed, overdueAt);
        _now = overdueAt.AddDays(7);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.InstallmentOverdueReminder && e.InstallmentNumber == 2)
            .Which.AmountCents.Should().Be(10_230, "2% fine + 9 days of interest");
        payment.FindInstallment(2)!.OverdueRemindedAt.Should().Be(_now);
    }

    [Fact]
    public async Task ReconcileAsync_Should_RecordAReminder_When_InstallmentIsDueSoon()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        _now = payment.FindInstallment(2)!.DueAt.AddDays(-2);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.InstallmentReminder && e.InstallmentNumber == 2);
    }

    [Fact]
    public async Task ReconcileAsync_Should_TransferToTheHostAccount_When_StayIsCompleted()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _stripe.Verify(s => s.TransferAsync(
            It.Is<TransferDraft>(d => d.DestinationAccountId == PaymentsTestData.HostAccountId && d.ChargeId == PaymentBuilder.ChargeId(1) && d.AmountCents == 27_000 && d.InstallmentId == payment.FindInstallment(1)!.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        payment.FindInstallment(1)!.StripeTransferId.Should().Be("tr_1");
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.PayoutTransferred && e.AmountCents == 27_000);
    }

    [Fact]
    public async Task ReconcileAsync_Should_KeepThePayoutHeld_When_HostHasNoConnectedAccount()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));
        _parties = PaymentsTestData.Parties(hostAccount: null);

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Held);
        payment.FindInstallment(1)!.PayoutFailureCode.Should().Be(PaymentReconciler.NoConnectedAccount);
        _stripe.Verify(s => s.TransferAsync(It.IsAny<TransferDraft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_Should_RecordTheFailure_When_StripeRejectsTheTransfer()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));
        _stripe.Setup(s => s.TransferAsync(It.IsAny<TransferDraft>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<string>.Failure(PaymentsErrors.StripeRequestRejected));

        // Act
        var result = await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("a failed request is retried by the maintenance");
        payment.FindInstallment(1)!.PayoutFailureCode.Should().Be(PaymentsErrors.StripeRequestRejected.Code);
        _effects.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_Should_RefundHalfAndThenTransferTheRest_When_OwnerCancelledLate()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled, refundPercentage: 50));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _stripe.Verify(s => s.RefundAsync(It.Is<RefundDraft>(d => d.AmountCents == 15_000), It.IsAny<CancellationToken>()), Times.Once);
        _stripe.Verify(s => s.TransferAsync(It.Is<TransferDraft>(d => d.AmountCents == 13_500), It.IsAny<CancellationToken>()), Times.Once);
        _effects.Items.Select(e => e.Kind).Should().Equal(PaymentEffectKind.RefundCreated, PaymentEffectKind.PayoutTransferred);
    }

    [Fact]
    public async Task ReconcileAsync_Should_AlertTheAdmin_When_StripeReportsTheRefundAsFailed()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled, refundPercentage: 100));
        _stripe.Setup(s => s.RefundAsync(It.IsAny<RefundDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RefundSnapshot>.Success(new RefundSnapshot("re_1", 30_000, RefundStatus.Failed)));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _effects.Items.Select(e => e.Kind).Should().Contain(PaymentEffectKind.RefundFailed);
        payment.FindInstallment(1)!.RefundStatus.Should().Be(RefundStatus.Failed);
    }

    [Fact]
    public async Task ReconcileAsync_Should_LeaveTheRefundForLater_When_StripeIsUnavailable()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled, refundPercentage: 100));
        _stripe.Setup(s => s.RefundAsync(It.IsAny<RefundDraft>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<RefundSnapshot>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.FindInstallment(1)!.RefundStatus.Should().Be(RefundStatus.None);
        _stripe.Verify(s => s.RefundAsync(It.IsAny<RefundDraft>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileAsync_Should_CancelTheOpenCharge_When_BookingIsCancelledBeforePaying()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _stripe.Verify(s => s.CancelIntentAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>()), Times.Once);
        payment.FindInstallment(1)!.IntentOpen.Should().BeFalse();
        payment.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReconcileAsync_Should_RefundInsteadOfCancelling_When_TheOwnerPaidAtTheLastSecond()
    {
        // Arrange — o cancelamento da cobrança é recusado porque ela acabou de ser paga.
        var payment = new PaymentBuilder().WithInstallments(1).Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled));
        _stripe.Setup(s => s.CancelIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.StripeRequestRejected));
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1, PaymentMethod.Pix)));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        payment.FindInstallment(1)!.IsPaid.Should().BeTrue();
        _stripe.Verify(s => s.RefundAsync(It.Is<RefundDraft>(d => d.AmountCents == 30_000), It.IsAny<CancellationToken>()), Times.Once);
        _effects.Items.Select(e => e.Kind).Should().Equal(PaymentEffectKind.InstallmentPaid, PaymentEffectKind.RefundCreated);
    }

    [Fact]
    public async Task ReconcileAsync_Should_ReverseTheTransfer_When_DisputeIsLostAfterThePayout()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 27_000, 3_000, Now);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Lost) }, Now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        _stripe.Verify(s => s.ReverseTransferAsync(It.Is<ReversalDraft>(d => d.TransferId == "tr_1" && d.AmountCents == 27_000), It.IsAny<CancellationToken>()), Times.Once);
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Reversed);
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.PayoutReversed);
    }

    [Fact]
    public async Task ReconcileAsync_Should_AlertOnlyOnce_When_TheReversalKeepsFailing()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 27_000, 3_000, Now);
        payment.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Lost) }, Now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));
        _stripe.Setup(s => s.ReverseTransferAsync(It.IsAny<ReversalDraft>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<string>.Failure(PaymentsErrors.StripeRequestRejected));

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);
        var secondRun = new PaymentEffects();
        await Sut().ReconcileAsync(payment, secondRun, CancellationToken.None);

        // Assert
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.PayoutReversalFailed);
        secondRun.Items.Should().BeEmpty();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Transferred);
    }

    [Fact]
    public async Task ReconcileAsync_Should_TreatAMissingBookingAsClosed()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _bookings = PaymentsTestData.Bookings((PaymentBooking?)null);

        // Act
        await Sut().ReconcileAsync(payment, _effects, CancellationToken.None);

        // Assert
        payment.Status.Should().Be(PaymentStatus.Cancelled);
        payment.RefundPercentage.Should().Be(100);
    }

    [Fact]
    public async Task RefreshAsync_Should_ApplyTheStripeSnapshotAndRecordThePayment()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1, PaymentMethod.Pix)));

        // Act
        var result = await Sut().RefreshAsync(payment, payment.FindInstallment(1)!, _effects, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.FindInstallment(1)!.IsPaid.Should().BeTrue();
        _effects.Items.Should().ContainSingle(e => e.Kind == PaymentEffectKind.InstallmentPaid && e.Detail == "pix");
    }

    [Fact]
    public async Task RefreshAsync_Should_ReturnTheFailure_When_StripeIsUnavailable()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _stripe.Setup(s => s.GetChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await Sut().RefreshAsync(payment, payment.FindInstallment(1)!, _effects, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
        payment.FindInstallment(1)!.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_Should_DoNothing_When_InstallmentHasNoCharge()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        var result = await Sut().RefreshAsync(payment, payment.FindInstallment(1)!, _effects, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _stripe.Verify(s => s.GetChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void NeedingRefresh_Should_PickOnlyWhatCanChangeWithoutAWebhook_When_RunByTheMaintenance()
    {
        // Arrange — 1ª paga e com reembolso a caminho; 2ª em processamento; 3ª sem cobrança.
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.RecordRefund(1, "re_1", 10_000, RefundStatus.Pending, Now);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), Now);
        payment.ApplyCharge(2, new ChargeSnapshot(ChargeState.Processing, PaymentMethod.Pix), Now);

        // Act
        var maintenance = PaymentReconciler.NeedingRefresh(payment, everything: false);
        var everything = PaymentReconciler.NeedingRefresh(payment, everything: true);

        // Assert
        maintenance.Select(i => i.Number).Should().Equal(1, 2);
        everything.Select(i => i.Number).Should().Equal(1, 2);
    }

    [Fact]
    public void NeedingRefresh_Should_RereadAnOpenCharge_When_TheWebhookMayHaveBeenLost()
    {
        // Arrange — o tutor abriu o checkout (QR code do Pix gerado) e talvez tenha pago.
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);

        // Act
        var maintenance = PaymentReconciler.NeedingRefresh(payment, everything: false);

        // Assert
        maintenance.Select(i => i.Number).Should().Equal(1);
    }
}
