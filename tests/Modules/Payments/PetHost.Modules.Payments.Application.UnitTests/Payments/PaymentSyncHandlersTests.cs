using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments;
using PetHost.Modules.Payments.Application.Payments.ListMyPayouts;
using PetHost.Modules.Payments.Application.Payments.ListPaymentIssues;
using PetHost.Modules.Payments.Application.Payments.ReconcileBookingPayment;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Application.Payments.RunPaymentsMaintenance;
using PetHost.Modules.Payments.Application.Payments.SyncBookingPayment;
using PetHost.Modules.Payments.Application.Payments.SyncPaymentIntent;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Application.UnitTests.Payments;

public sealed class SyncPaymentIntentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow.AddHours(2);

    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private readonly Mock<IPaymentsUnitOfWork> _unitOfWork = PaymentsTestData.UnitOfWork();
    private readonly Mock<IPaymentNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Payment _payment = new PaymentBuilder().Build();
    private readonly Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(), PaymentsTestData.Booking(BookingStage.Confirmed));

    public SyncPaymentIntentCommandHandlerTests() => _payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);

    private SyncPaymentIntentCommandHandler Sut()
    {
        var parties = PaymentsTestData.Parties();
        return new(
            PaymentsTestData.Repository(_payment).Object,
            _stripe.Object,
            new PaymentReconciler(_stripe.Object, _bookings.Object, parties.Object, new FakeTimeProvider(Now)),
            new PaymentCommitter(_unitOfWork.Object, PaymentsTestData.Publisher(_notifier, _auditTrail)));
    }

    [Fact]
    public async Task HandleAsync_Should_MarkPaidConfirmTheBookingAndEmailTheOwner_When_StripeConfirmsThePayment()
    {
        // Arrange
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1, PaymentMethod.Pix)));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand(PaymentBuilder.IntentId(1)), CancellationToken.None);

        // Assert
        result.Value!.Matched.Should().BeTrue();
        _payment.FindInstallment(1)!.IsPaid.Should().BeTrue();
        _bookings.Verify(b => b.ConfirmPaidAsync(PaymentBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyAsync(It.Is<PaymentNotice>(x => x.Kind == PaymentNoticeKind.InstallmentPaid && x.Email == PaymentsTestData.OwnerContact.Email && x.AmountCents == 10_000), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PaymentInstallmentPaid && r.Details!["detail"] == "pix"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ResolveTheCharge_When_TheEventIsAboutADispute()
    {
        // Arrange
        _stripe.Setup(s => s.ResolveIntentIdAsync("dp_1", It.IsAny<CancellationToken>())).ReturnsAsync(Result<string?>.Success(PaymentBuilder.IntentId(1)));
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(new ChargeSnapshot(ChargeState.AwaitingPayment)));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand("dp_1"), CancellationToken.None);

        // Assert
        result.Value!.Matched.Should().BeTrue();
        _stripe.Verify(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_IgnoreTheEvent_When_TheObjectHasNoCharge()
    {
        // Arrange
        _stripe.Setup(s => s.ResolveIntentIdAsync("re_orphan", It.IsAny<CancellationToken>())).ReturnsAsync(Result<string?>.Success(null));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand("re_orphan"), CancellationToken.None);

        // Assert
        result.Value!.Matched.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_IgnoreTheEvent_When_TheChargeIsNotFromAnyInstallment()
    {
        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand("pi_someone_else"), CancellationToken.None);

        // Assert
        result.Value!.Matched.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_FailWithoutSaving_When_StripeIsUnavailable()
    {
        // Arrange
        _stripe.Setup(s => s.GetChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand(PaymentBuilder.IntentId(1)), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_FailWhenResolutionFails_When_StripeIsUnavailable()
    {
        // Arrange
        _stripe.Setup(s => s.ResolveIntentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<string?>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand("ch_1"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
    }

    [Fact]
    public async Task HandleAsync_Should_NotPublish_When_SaveConflicts()
    {
        // Arrange
        _stripe.Setup(s => s.GetChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1)));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.Conflict));

        // Act
        var result = await Sut().HandleAsync(new SyncPaymentIntentCommand(PaymentBuilder.IntentId(1)), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Conflict);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public void Validator_Should_RequireTheStripeObjectId()
    {
        // Act
        var result = new SyncPaymentIntentCommandValidator().Validate(new SyncPaymentIntentCommand(""));

        // Assert
        result.IsValid.Should().BeFalse();
    }
}

public sealed class SyncBookingPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow.AddHours(2);

    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private readonly Mock<IPaymentsUnitOfWork> _unitOfWork = PaymentsTestData.UnitOfWork();
    private Payment? _payment = new PaymentBuilder().Build();

    // O acesso lê a reserva (aceita), a reconciliação lê de novo (aceita) e, depois de confirmar, confirmada.
    private readonly Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(), PaymentsTestData.Booking(), PaymentsTestData.Booking(BookingStage.Confirmed));

    private SyncBookingPaymentCommandHandler Sut()
    {
        var parties = PaymentsTestData.Parties();
        return new(
            PaymentsTestData.Repository(_payment).Object,
            _bookings.Object,
            parties.Object,
            new PaymentReconciler(_stripe.Object, _bookings.Object, parties.Object, new FakeTimeProvider(Now)),
            new PaymentCommitter(_unitOfWork.Object, PaymentsTestData.Publisher()),
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_Should_RereadOpenChargesAndConfirm_When_OwnerReturnsFromThePaymentElement()
    {
        // Arrange
        _payment!.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1)));

        // Act
        var result = await Sut().HandleAsync(new SyncBookingPaymentCommand(PaymentsTestData.OwnerUserId, Roles.Owner, PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("active");
        result.Value.Installments[0].Status.Should().Be("paid");
        result.Value.Options.Should().BeNull("the booking is confirmed now");
        _bookings.Verify(b => b.ConfirmPaidAsync(PaymentBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnStripeUnavailable_When_TheReadFails()
    {
        // Arrange
        _payment!.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _stripe.Setup(s => s.GetChargeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await Sut().HandleAsync(new SyncBookingPaymentCommand(PaymentsTestData.OwnerUserId, Roles.Owner, PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ThereIsNoPayment()
    {
        // Arrange
        _payment = null;

        // Act
        var result = await Sut().HandleAsync(new SyncBookingPaymentCommand(PaymentsTestData.OwnerUserId, Roles.Owner, PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.NotFound(PaymentBuilder.BookingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBookingNotFound_When_AnotherOwnerAsks()
    {
        // Act
        var result = await Sut().HandleAsync(new SyncBookingPaymentCommand(PaymentsTestData.OtherOwnerUserId, Roles.Owner, PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.BookingNotFound(PaymentBuilder.BookingId));
    }
}

public sealed class ReconcileBookingPaymentCommandHandlerTests
{
    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private readonly Mock<IPaymentsUnitOfWork> _unitOfWork = PaymentsTestData.UnitOfWork();

    private ReconcileBookingPaymentCommandHandler Sut(Payment? payment, Mock<IPaymentBookings> bookings)
    {
        var parties = PaymentsTestData.Parties();
        return new(
            PaymentsTestData.Repository(payment).Object,
            new PaymentReconciler(_stripe.Object, bookings.Object, parties.Object, new FakeTimeProvider(PaymentBuilder.DefaultNow.AddDays(10))),
            new PaymentCommitter(_unitOfWork.Object, PaymentsTestData.Publisher()));
    }

    [Fact]
    public async Task HandleAsync_Should_TransferAndSave_When_StayWasCompleted()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);

        // Act
        var result = await Sut(payment, PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed)))
            .HandleAsync(new ReconcileBookingPaymentCommand(PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.FindInstallment(1)!.PayoutStatus.Should().Be(PayoutStatus.Transferred);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_DoNothing_When_TheBookingHasNoPayment()
    {
        // Act
        var result = await Sut(null, PaymentsTestData.Bookings()).HandleAsync(new ReconcileBookingPaymentCommand(PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_SaveConflicts()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(1).BuildPaid(1);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.Conflict));

        // Act
        var result = await Sut(payment, PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed)))
            .HandleAsync(new ReconcileBookingPaymentCommand(PaymentBuilder.BookingId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Conflict);
    }
}

public sealed class RunPaymentsMaintenanceCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_Should_RereadAndReconcileEachUnsettledPaymentAndCountFailures()
    {
        // Arrange — um pagamento com o Pix compensando (o webhook se perdeu) e um cuja reserva sumiu.
        var processing = new PaymentBuilder().WithInstallments(1).Build();
        processing.AttachIntent(1, PaymentBuilder.IntentId(1), PaymentBuilder.DefaultNow);
        processing.ApplyCharge(1, new ChargeSnapshot(ChargeState.Processing, PaymentMethod.Pix), PaymentBuilder.DefaultNow);

        var repository = PaymentsTestData.Repository(processing);
        var missing = Guid.CreateVersion7();
        repository.Setup(r => r.ListUnsettledBookingIdsAsync(RunPaymentsMaintenanceCommand.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([processing.BookingId, missing]);

        var stripe = PaymentsTestData.Stripe();
        stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>())).ReturnsAsync(Result<ChargeSnapshot>.Failure(PaymentsErrors.StripeUnavailable));

        var unitOfWork = PaymentsTestData.UnitOfWork();
        var bookings = PaymentsTestData.Bookings();
        var parties = PaymentsTestData.Parties();
        var sut = new RunPaymentsMaintenanceCommandHandler(
            repository.Object,
            new PaymentReconciler(stripe.Object, bookings.Object, parties.Object, new FakeTimeProvider(PaymentBuilder.DefaultNow.AddHours(1))),
            new PaymentCommitter(unitOfWork.Object, PaymentsTestData.Publisher()));

        // Act
        var result = await sut.HandleAsync(new RunPaymentsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RunPaymentsMaintenanceResponse(Reviewed: 2, Failed: 1));
        stripe.Verify(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class ListMyPayoutsQueryHandlerTests
{
    private readonly Mock<IPaymentRepository> _repository = PaymentsTestData.Repository();

    private ListMyPayoutsQueryHandler Sut() => new(_repository.Object, PaymentsTestData.Parties().Object);

    [Fact]
    public async Task HandleAsync_Should_ListEachPaidBookingWithTheHostShare()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.RecordTransfer(1, "tr_1", 9_000, 1_000, PaymentBuilder.DefaultNow.AddDays(10));
        _repository.Setup(r => r.SearchPaidByHostAsync(BookingBuilder.HostId, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([payment], 1));

        // Act
        var result = await Sut().HandleAsync(new ListMyPayoutsQuery(PaymentsTestData.HostUserId), CancellationToken.None);

        // Assert
        var page = result.Value!;
        page.TotalCount.Should().Be(1);
        var earnings = page.Items[0];
        earnings.Payout.Should().Be(new PayoutSummaryResponse(90m, 0m, 0m));
        earnings.Installments.Select(i => (i.Number, i.PayoutStatus, i.HostAmount)).Should().Equal((1, "transferred", 90m), (2, "held", 90m), (3, "held", 90m));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryPagingError_When_PagingIsInvalid()
    {
        // Act
        var result = await Sut().HandleAsync(new ListMyPayoutsQuery(PaymentsTestData.HostUserId, 0, 101), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([PaymentsErrors.PageInvalid, PaymentsErrors.PageSizeInvalid]);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_AccountHasNoHostProfile()
    {
        // Act
        var result = await Sut().HandleAsync(new ListMyPayoutsQuery(PaymentsTestData.NoProfileUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.HostNotFound);
    }
}

public sealed class ListPaymentIssuesQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_Should_ListEveryIssueOfEveryInstallment()
    {
        // Arrange
        var now = PaymentBuilder.DefaultNow;
        var disputed = new PaymentBuilder().WithInstallments(2).BuildPaid(1);
        disputed.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Open) }, now.AddDays(1));
        disputed.CheckOverdue(BookingStage.Completed, disputed.FindInstallment(2)!.DueAt + Payment.OverdueGrace);

        var failedPayout = new PaymentBuilder().ForBooking(Guid.CreateVersion7()).WithInstallments(1).BuildPaid(1);
        failedPayout.RecordTransferFailed(1, "NO_CONNECTED_ACCOUNT", now.AddDays(10));

        var failedRefund = new PaymentBuilder().ForBooking(Guid.CreateVersion7()).WithInstallments(1).BuildPaid(1);
        failedRefund.RecordRefund(1, "re_1", 30_000, RefundStatus.Failed, now.AddDays(2));

        var blockedPayout = new PaymentBuilder().ForBooking(Guid.CreateVersion7()).WithInstallments(1).BuildPaid(1);
        blockedPayout.RecordTransferFailed(1, PaymentReconciler.HostPayoutsDisabled, now.AddDays(10));

        var repository = PaymentsTestData.Repository();
        repository.Setup(r => r.ListNeedingAttentionAsync(It.IsAny<CancellationToken>())).ReturnsAsync([disputed, failedPayout, failedRefund, blockedPayout]);

        // Act
        var result = await new ListPaymentIssuesQueryHandler(repository.Object).HandleAsync(new ListPaymentIssuesQuery(), CancellationToken.None);

        // Assert
        result.Value!.Select(i => i.Issue).Should().BeEquivalentTo(["dispute_open", "installment_overdue", "payout_failed", "refund_failed", "payout_blocked"]);
        result.Value!.Single(i => i.Issue == "payout_failed").FailureCode.Should().Be("NO_CONNECTED_ACCOUNT");
        result.Value!.Single(i => i.Issue == "payout_blocked").FailureCode.Should().Be("HOST_PAYOUTS_DISABLED");
        result.Value!.Single(i => i.Issue == "dispute_open").StripeObjectId.Should().Be("dp_1");
    }
}

public sealed class PaymentEffectsPublisherTests
{
    private readonly Mock<IPaymentNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Payment _payment = new PaymentBuilder().BuildPaid(1);

    private Task PublishAsync(PaymentEffectKind kind, string? detail = null)
    {
        var effects = new PaymentEffects();
        effects.Add(kind, 1, 10_000, detail);

        return PaymentsTestData.Publisher(_notifier, _auditTrail).PublishAsync(_payment, effects, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_Should_EmailTheHostAndRecord_When_PayoutWasTransferred()
    {
        // Act
        await PublishAsync(PaymentEffectKind.PayoutTransferred);

        // Assert
        _notifier.Verify(n => n.NotifyAsync(It.Is<PaymentNotice>(x => x.Kind == PaymentNoticeKind.PayoutTransferred && x.Email == PaymentsTestData.HostContact.Email), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PaymentPayoutTransferred && r.TargetId == _payment.Id.Value && r.Details!["amountCents"] == "10000"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Should_AlertTheAdminAndRecord_When_DisputeIsOpened()
    {
        // Act
        await PublishAsync(PaymentEffectKind.DisputeOpened);

        // Assert
        _notifier.Verify(n => n.AlertAdminAsync(It.Is<PaymentAlert>(a => a.Issue == "dispute_opened" && a.BookingId == PaymentBuilder.BookingId), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<PaymentNotice>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PaymentDisputeOpened), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Should_EmailTheOwnerAndAlertTheAdmin_When_InstallmentIsOverdue()
    {
        // Act
        await PublishAsync(PaymentEffectKind.InstallmentOverdue);

        // Assert
        _notifier.Verify(n => n.NotifyAsync(It.Is<PaymentNotice>(x => x.Kind == PaymentNoticeKind.InstallmentOverdue && x.Email == PaymentsTestData.OwnerContact.Email && x.DueAt != null), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.AlertAdminAsync(It.Is<PaymentAlert>(a => a.Issue == "installment_overdue"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Should_OnlyAlertTheAdmin_When_ThePayoutIsBlocked()
    {
        // Act
        await PublishAsync(PaymentEffectKind.PayoutBlocked);

        // Assert
        _notifier.Verify(n => n.AlertAdminAsync(It.Is<PaymentAlert>(a => a.Issue == "payout_blocked" && a.AmountCents == 10_000), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<PaymentNotice>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_Should_OnlyEmail_When_ItIsAReminder()
    {
        // Act
        await PublishAsync(PaymentEffectKind.InstallmentReminder);

        // Assert
        _notifier.Verify(n => n.NotifyAsync(It.Is<PaymentNotice>(x => x.Kind == PaymentNoticeKind.InstallmentReminder), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsync_Should_OnlyAlertTheAdmin_When_TheOverdueIsEscalated()
    {
        // Act
        await PublishAsync(PaymentEffectKind.InstallmentOverdueEscalated);

        // Assert
        _notifier.Verify(n => n.AlertAdminAsync(It.Is<PaymentAlert>(a => a.Issue == "installment_overdue_escalated" && a.AmountCents == 10_000), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<PaymentNotice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_Should_OnlyEmailTheOwner_When_ItIsTheWeeklyOverdueReminder()
    {
        // Act
        await PublishAsync(PaymentEffectKind.InstallmentOverdueReminder);

        // Assert
        _notifier.Verify(n => n.NotifyAsync(It.Is<PaymentNotice>(x => x.Kind == PaymentNoticeKind.InstallmentOverdueReminder && x.Email == PaymentsTestData.OwnerContact.Email && x.AmountCents == 10_000), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.AlertAdminAsync(It.IsAny<PaymentAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsync_Should_AlertTheOutcome_When_DisputeIsClosed()
    {
        // Act
        await PublishAsync(PaymentEffectKind.DisputeClosed, "lost");

        // Assert
        _notifier.Verify(n => n.AlertAdminAsync(It.Is<PaymentAlert>(a => a.Issue == "dispute_lost"), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PaymentDisputeClosed && r.Details!["detail"] == "lost"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_Should_DoNothing_When_ThereAreNoEffects()
    {
        // Act
        await PaymentsTestData.Publisher(_notifier, _auditTrail).PublishAsync(_payment, new PaymentEffects(), CancellationToken.None);

        // Assert
        _notifier.VerifyNoOtherCalls();
        _auditTrail.VerifyNoOtherCalls();
    }
}
