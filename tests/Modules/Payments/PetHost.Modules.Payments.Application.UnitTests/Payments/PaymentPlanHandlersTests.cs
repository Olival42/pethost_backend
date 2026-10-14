using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments;
using PetHost.Modules.Payments.Application.Payments.ChoosePaymentPlan;
using PetHost.Modules.Payments.Application.Payments.GetBookingPayment;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Application.Payments.StartInstallmentPayment;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Application.UnitTests.Payments;

public sealed class ChoosePaymentPlanCommandHandlerTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow.AddHours(1);

    private readonly Mock<IPaymentsUnitOfWork> _unitOfWork = PaymentsTestData.UnitOfWork();
    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private Mock<IPaymentRepository> _repository = PaymentsTestData.Repository();
    private Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings();
    private DateTimeOffset _now = Now;

    private ChoosePaymentPlanCommandHandler Sut() => new(
        _repository.Object,
        _unitOfWork.Object,
        _bookings.Object,
        PaymentsTestData.Parties().Object,
        _stripe.Object,
        PaymentsTestData.Publisher(auditTrail: _auditTrail),
        new FakeTimeProvider(_now));

    private Task<Result<PaymentResponse>> ChooseAsync(int count, Guid? userId = null) =>
        Sut().HandleAsync(new ChoosePaymentPlanCommand(userId ?? PaymentsTestData.OwnerUserId, PaymentBuilder.BookingId, count), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Should_CreateThePlanSaveAndRecord_When_BookingAwaitsPayment()
    {
        // Act
        var result = await ChooseAsync(3);

        // Assert
        var response = result.Value!;
        response.Status.Should().Be("pending");
        response.InstallmentCount.Should().Be(3);
        response.Installments.Select(i => i.Amount).Should().Equal(100m, 100m, 100m);
        response.PaymentDeadline.Should().Be(PaymentBuilder.DefaultDeadline);
        response.Options.Should().HaveCount(6);
        response.Payout.Should().BeNull();
        _repository.Verify(r => r.Add(It.Is<Payment>(p => p.InstallmentCount == 3 && p.BookingId == PaymentBuilder.BookingId)), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PaymentPlanChosen && r.TargetType == AuditTargets.Payment), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_CancelTheOldChargesAndReplaceThePlan_When_NothingWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _repository = PaymentsTestData.Repository(payment);

        // Act
        var result = await ChooseAsync(1);

        // Assert
        result.Value!.InstallmentCount.Should().Be(1);
        _stripe.Verify(s => s.CancelIntentAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Never);
        payment.PlanRevision.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPlanLocked_When_TheOldChargeWasPaidMeanwhile()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _repository = PaymentsTestData.Repository(payment);
        _stripe.Setup(s => s.CancelIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.StripeRequestRejected));

        // Act
        var result = await ChooseAsync(1);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PlanLocked);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnStripeUnavailable_When_TheOldChargeCannotBeCancelled()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _repository = PaymentsTestData.Repository(payment);
        _stripe.Setup(s => s.CancelIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await ChooseAsync(1);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPlanLocked_When_AnInstallmentWasPaid()
    {
        // Arrange
        _repository = PaymentsTestData.Repository(new PaymentBuilder().BuildPaid(1));

        // Act
        var result = await ChooseAsync(1);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PlanLocked);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPlanLocked_When_BookingIsAlreadyConfirmed()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed));

        // Act
        var result = await ChooseAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PlanLocked);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotAwaitingPayment_When_HostHasNotAcceptedYet()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Requested));

        // Act
        var result = await ChooseAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.BookingNotAwaitingPayment);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnClosed_When_BookingWasCancelled()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled));

        // Act
        var result = await ChooseAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Closed);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnWindowClosed_When_TheDeadlinePassed()
    {
        // Arrange
        _now = PaymentBuilder.DefaultDeadline.AddMinutes(1);

        // Act
        var result = await ChooseAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PaymentWindowClosed);
        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnCountNotAllowed_When_InstallmentsWouldBeBelowFiftyReais()
    {
        // Arrange — R$ 120,00 permite 2 parcelas.
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(amountCents: 12_000));

        // Act
        var result = await ChooseAsync(3);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentCountNotAllowed(2));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBookingNotFound_When_BookingIsFromAnotherOwner()
    {
        // Act
        var result = await ChooseAsync(2, PaymentsTestData.OtherOwnerUserId);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.BookingNotFound(PaymentBuilder.BookingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_AccountHasNoOwnerProfile()
    {
        // Act
        var result = await ChooseAsync(2, PaymentsTestData.NoProfileUserId);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.OwnerNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_SaveConflicts()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(PaymentsErrors.Conflict));

        // Act
        var result = await ChooseAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Conflict);
        _auditTrail.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Validator_Should_RejectInstallmentCountOutsideOneToSix(int count)
    {
        // Act
        var result = new ChoosePaymentPlanCommandValidator().Validate(new ChoosePaymentPlanCommand(Guid.NewGuid(), Guid.NewGuid(), count));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == PaymentsErrors.InstallmentCountInvalid.Message);
    }
}

public sealed class StartInstallmentPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow.AddHours(1);

    private readonly Mock<IPaymentsUnitOfWork> _unitOfWork = PaymentsTestData.UnitOfWork();
    private readonly Mock<IStripePayments> _stripe = PaymentsTestData.Stripe();
    private Payment _payment = new PaymentBuilder().Build();
    private Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings();
    private DateTimeOffset _now = Now;

    private StartInstallmentPaymentCommandHandler Sut()
    {
        var parties = PaymentsTestData.Parties();
        var time = new FakeTimeProvider(_now);

        return new(
            PaymentsTestData.Repository(_payment).Object,
            _unitOfWork.Object,
            _bookings.Object,
            parties.Object,
            _stripe.Object,
            new PaymentReconciler(_stripe.Object, _bookings.Object, parties.Object, time),
            PaymentsTestData.Publisher(),
            time);
    }

    private Task<Result<PaymentSessionResponse>> StartAsync(int number = 1) =>
        Sut().HandleAsync(new StartInstallmentPaymentCommand(PaymentsTestData.OwnerUserId, PaymentBuilder.BookingId, number), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Should_CreateACardAndPixChargeForTheInstallment_When_NoneIsOpen()
    {
        // Act
        var result = await StartAsync();

        // Assert
        var session = result.Value!;
        session.ClientSecret.Should().Be("pi_new_1_secret");
        session.PublishableKey.Should().Be("pk_test_pethost");
        session.Amount.Should().Be(100m);
        session.PaymentMethods.Should().Equal("card", "pix");
        session.PixExpiresAt.Should().Be(PaymentBuilder.DefaultDeadline.ToUnixTimeSeconds());
        _stripe.Verify(s => s.CreateIntentAsync(
            It.Is<IntentDraft>(d => d.InstallmentId == _payment.FindInstallment(1)!.Id && d.Attempt == 1 && d.AmountCents == 10_000 && d.PixExpiresAt == PaymentBuilder.DefaultDeadline),
            It.IsAny<CancellationToken>()), Times.Once);
        _payment.FindInstallment(1)!.StripePaymentIntentId.Should().Be("pi_new_1");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReuseTheOpenCharge_When_TheOwnerComesBack()
    {
        // Arrange
        _payment.AttachIntent(1, "pi_open", Now);
        _stripe.Setup(s => s.PrepareIntentAsync("pi_open", 10_000, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeCheckout>.Success(new StripeCheckout("pi_open", "pi_open_secret", ChargeState.AwaitingPayment, 10_000)));

        // Act
        var result = await StartAsync();

        // Assert
        result.Value!.ClientSecret.Should().Be("pi_open_secret");
        result.Value.LateFee.Should().Be(0m);
        _stripe.Verify(s => s.CreateIntentAsync(It.IsAny<IntentDraft>(), It.IsAny<CancellationToken>()), Times.Never);
        _payment.FindInstallment(1)!.LateFeeCents.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_Should_CreateAnotherCharge_When_TheOpenOneWasCanceledAtStripe()
    {
        // Arrange
        _payment.AttachIntent(1, "pi_canceled", Now);
        _stripe.Setup(s => s.PrepareIntentAsync("pi_canceled", It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeCheckout>.Success(new StripeCheckout("pi_canceled", "secret", ChargeState.Canceled, 10_000)));

        // Act
        var result = await StartAsync();

        // Assert
        result.Value!.PaymentIntentId.Should().Be("pi_new_1");
        _stripe.Verify(s => s.CreateIntentAsync(It.Is<IntentDraft>(d => d.Attempt == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SyncAndReturnAlreadyPaid_When_StripeAlreadyReceivedThePayment()
    {
        // Arrange
        _payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(), PaymentsTestData.Booking(), PaymentsTestData.Booking(BookingStage.Confirmed));
        _stripe.Setup(s => s.PrepareIntentAsync(PaymentBuilder.IntentId(1), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeCheckout>.Success(new StripeCheckout(PaymentBuilder.IntentId(1), "secret", ChargeState.Succeeded, 10_000)));
        _stripe.Setup(s => s.GetChargeAsync(PaymentBuilder.IntentId(1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChargeSnapshot>.Success(PaymentBuilder.Succeeded(1)));

        // Act
        var result = await StartAsync();

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentAlreadyPaid(1));
        _payment.FindInstallment(1)!.IsPaid.Should().BeTrue();
        _bookings.Verify(b => b.ConfirmPaidAsync(PaymentBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ChargeTheFineAndInterest_When_TheInstallmentIsOverdue()
    {
        // Arrange — estadia concluída, 2ª parcela vencida e passada a tolerância (2 dias de atraso).
        _payment = new PaymentBuilder().BuildPaid(1);
        _now = _payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        _payment.CheckOverdue(BookingStage.Completed, _now);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));

        // Act
        var result = await StartAsync(2);

        // Assert
        var session = result.Value!;
        session.Amount.Should().Be(102.07m);
        session.LateFee.Should().Be(2.07m);
        _stripe.Verify(s => s.CreateIntentAsync(
            It.Is<IntentDraft>(d => d.InstallmentNumber == 2 && d.AmountCents == 10_207 && d.LateFeeCents == 207),
            It.IsAny<CancellationToken>()), Times.Once);
        _payment.FindInstallment(2)!.LateFeeCents.Should().Be(207);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_UpdateTheOpenChargeToTodaysAmount_When_TheOverdueOwnerComesBack()
    {
        // Arrange — a cobrança foi aberta antes e o atraso já tem 9 dias.
        _payment = new PaymentBuilder().BuildPaid(1);
        var overdueAt = _payment.FindInstallment(2)!.DueAt + Payment.OverdueGrace;
        _payment.CheckOverdue(BookingStage.Completed, overdueAt);
        _payment.AttachIntent(2, "pi_late", overdueAt);
        _payment.RecordCharge(2, 10_207, overdueAt);
        _now = overdueAt.AddDays(7);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Completed));
        _stripe.Setup(s => s.PrepareIntentAsync("pi_late", 10_230, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeCheckout>.Success(new StripeCheckout("pi_late", "pi_late_secret", ChargeState.AwaitingPayment, 10_230)));

        // Act
        var result = await StartAsync(2);

        // Assert
        result.Value!.Amount.Should().Be(102.30m);
        result.Value.LateFee.Should().Be(2.30m);
        _payment.FindInstallment(2)!.LateFeeCents.Should().Be(230);
        _stripe.Verify(s => s.CreateIntentAsync(It.IsAny<IntentDraft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOutOfOrder_When_TheFirstInstallmentIsOpen()
    {
        // Act
        var result = await StartAsync(2);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentOutOfOrder(1));
        _stripe.Verify(s => s.CreateIntentAsync(It.IsAny<IntentDraft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ThePlanWasNotChosen()
    {
        // Arrange
        _payment = new PaymentBuilder().ForBooking(Guid.CreateVersion7()).Build();

        // Act
        var result = await StartAsync();

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.NotFound(PaymentBuilder.BookingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnClosed_When_BookingWasCancelled()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Cancelled));

        // Act
        var result = await StartAsync();

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Closed);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnStripeUnavailable_When_TheChargeCannotBeCreated()
    {
        // Arrange
        _stripe.Setup(s => s.CreateIntentAsync(It.IsAny<IntentDraft>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<StripeCheckout>.Failure(PaymentsErrors.StripeUnavailable));

        // Act
        var result = await StartAsync();

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.StripeUnavailable);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Validator_Should_RejectInstallmentNumberOutsideOneToSix(int number)
    {
        // Act
        var result = new StartInstallmentPaymentCommandValidator().Validate(new StartInstallmentPaymentCommand(Guid.NewGuid(), Guid.NewGuid(), number));

        // Assert
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == PaymentsErrors.InstallmentNumberInvalid.Message);
    }
}

public sealed class GetBookingPaymentQueryHandlerTests
{
    private Payment? _payment;
    private Mock<IPaymentBookings> _bookings = PaymentsTestData.Bookings();

    private Task<Result<PaymentResponse>> GetAsync(Guid userId, string role) =>
        new GetBookingPaymentQueryHandler(PaymentsTestData.Repository(_payment).Object, _bookings.Object, PaymentsTestData.Parties().Object, new FakeTimeProvider(PaymentBuilder.DefaultNow))
            .HandleAsync(new GetBookingPaymentQuery(userId, role, PaymentBuilder.BookingId), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Should_OfferTheInstallmentOptions_When_OwnerHasNotChosenYet()
    {
        // Act
        var result = await GetAsync(PaymentsTestData.OwnerUserId, Roles.Owner);

        // Assert
        var response = result.Value!;
        response.Id.Should().BeNull();
        response.Status.Should().Be(PaymentResponseMapper.NotStarted);
        response.TotalAmount.Should().Be(300m);
        response.PaymentDeadline.Should().Be(PaymentBuilder.DefaultDeadline);
        response.Options!.Select(o => o.Count).Should().Equal(1, 2, 3, 4, 5, 6);
        response.Options.Should().OnlyContain(o => o.Installments.Count == o.Count);
        response.Options.Single(o => o.Count == 3).Installments.Should().Equal(
            new PlannedInstallmentResponse(1, 100m, PaymentBuilder.DefaultDeadline),
            new PlannedInstallmentResponse(2, 100m, new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero)),
            new PlannedInstallmentResponse(3, 100m, new DateTimeOffset(2026, 12, 11, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task HandleAsync_Should_ShowThePayoutWithoutOptions_When_HostLooks()
    {
        // Arrange
        _payment = new PaymentBuilder().BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed));

        // Act
        var result = await GetAsync(PaymentsTestData.HostUserId, Roles.Host);

        // Assert
        var response = result.Value!;
        response.Options.Should().BeNull();
        response.Payout.Should().Be(new PayoutSummaryResponse(0m, 90m, 0m));
        response.Installments[0].Stripe.Should().BeNull();
        response.PaidAmount.Should().Be(100m);
    }

    [Fact]
    public async Task HandleAsync_Should_ShowTheStripeIds_When_AdminLooks()
    {
        // Arrange
        _payment = new PaymentBuilder().BuildPaid(1);
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Confirmed));

        // Act
        var result = await GetAsync(Guid.CreateVersion7(), Roles.Admin);

        // Assert
        result.Value!.Installments[0].Stripe!.PaymentIntentId.Should().Be(PaymentBuilder.IntentId(1));
        result.Value.Installments[0].Stripe!.ChargeId.Should().Be(PaymentBuilder.ChargeId(1));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPaymentNotFound_When_BookingIsNotAwaitingPaymentAndHasNoPlan()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings(PaymentsTestData.Booking(BookingStage.Closed));

        // Act
        var result = await GetAsync(PaymentsTestData.OwnerUserId, Roles.Owner);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.NotFound(PaymentBuilder.BookingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBookingNotFound_When_AnotherOwnerLooks()
    {
        // Act
        var result = await GetAsync(PaymentsTestData.OtherOwnerUserId, Roles.Owner);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.BookingNotFound(PaymentBuilder.BookingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_AccountHasNoHostProfile()
    {
        // Act
        var result = await GetAsync(PaymentsTestData.NoProfileUserId, Roles.Host);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.HostNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBookingNotFound_When_BookingDoesNotExist()
    {
        // Arrange
        _bookings = PaymentsTestData.Bookings((PaymentBooking?)null);

        // Act
        var result = await GetAsync(Guid.CreateVersion7(), Roles.Admin);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.BookingNotFound(PaymentBuilder.BookingId));
    }
}
