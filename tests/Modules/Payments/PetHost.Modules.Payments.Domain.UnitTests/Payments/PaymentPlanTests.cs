using FluentAssertions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Domain.UnitTests.Payments;

/// <summary>O plano de parcelas: opções, divisão, vencimentos, troca e quem pode ser pago agora.</summary>
public sealed class PaymentPlanTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow;

    [Fact]
    public void OptionsFor_Should_OfferOneToSixInstallments_When_EachIsAtLeastFiftyReais()
    {
        // Act
        var options = Payment.OptionsFor(30_000);

        // Assert
        options.Select(o => o.Count).Should().Equal(1, 2, 3, 4, 5, 6);
        options[^1].Should().Be(new InstallmentOption(6, 5_000, 5_000));
    }

    [Fact]
    public void OptionsFor_Should_OfferOnlyFullPayment_When_AmountIsBelowTwoMinimumInstallments()
    {
        // Act
        var options = Payment.OptionsFor(9_999);

        // Assert
        options.Should().Equal(new InstallmentOption(1, 9_999, 9_999));
    }

    [Fact]
    public void OptionsFor_Should_PutTheLeftoverCentsInTheFirstInstallment_When_AmountDoesNotDivideEvenly()
    {
        // Act
        var options = Payment.OptionsFor(30_001);

        // Assert
        options.Single(o => o.Count == 3).Should().Be(new InstallmentOption(3, 10_001, 10_000));
    }

    [Fact]
    public void OptionsFor_Should_Throw_When_AmountIsNotPositive()
    {
        // Act
        var act = () => Payment.OptionsFor(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ScheduleFor_Should_ListEachInstallmentWithItsDueDate_When_CountIsAllowed()
    {
        // Act
        var schedule = Payment.ScheduleFor(30_001, 3, PaymentBuilder.DefaultDeadline, Now);

        // Assert
        schedule.Should().Equal(
            new PlannedInstallment(1, 10_001, PaymentBuilder.DefaultDeadline),
            new PlannedInstallment(2, 10_000, new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero)),
            new PlannedInstallment(3, 10_000, new DateTimeOffset(2026, 12, 11, 3, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ScheduleFor_Should_MatchTheInstallmentsCreated_When_TheTutorChoosesThatCount()
    {
        // Arrange
        var payment = new PaymentBuilder().WithAmount(30_000).WithInstallments(6).Build();

        // Act
        var schedule = Payment.ScheduleFor(30_000, 6, PaymentBuilder.DefaultDeadline, Now);

        // Assert
        schedule.Select(p => (p.Number, p.AmountCents, p.DueAt)).Should()
            .Equal(payment.Installments.Select(i => (i.Number, i.AmountCents, i.DueAt)));
    }

    [Fact]
    public void ScheduleFor_Should_Throw_When_CountIsNotAllowedForTheAmount()
    {
        // Act
        var act = () => Payment.ScheduleFor(9_999, 2, PaymentBuilder.DefaultDeadline, Now);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Should_SplitTheAmountAndScheduleMonthlyDueDates_When_CountIsAllowed()
    {
        // Act
        var payment = new PaymentBuilder().WithAmount(30_001).WithInstallments(3).Build();

        // Assert
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.InstallmentCount.Should().Be(3);
        payment.PlanRevision.Should().Be(1);
        payment.Installments.Select(i => i.AmountCents).Should().Equal(10_001, 10_000, 10_000);
        payment.Installments.Select(i => i.DueAt).Should().Equal(
            PaymentBuilder.DefaultDeadline,
            new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 11, 3, 0, 0, TimeSpan.Zero));
        payment.Installments.Should().OnlyContain(i => i.Status == InstallmentStatus.Pending && i.PayoutStatus == PayoutStatus.Held);
        payment.SettledAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    public void Create_Should_ReturnCountNotAllowed_When_CountIsOutsideWhatTheAmountAllows(int count)
    {
        // Act — R$ 150,00 permite até 3 parcelas de R$ 50,00.
        var result = Payment.Create(new PaymentBuilder().WithAmount(15_000).Booking(), count, Now);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentCountNotAllowed(3));
    }

    [Fact]
    public void Create_Should_Throw_When_AmountIsNotPositive()
    {
        // Act
        var act = () => Payment.Create(new PaymentBuilder().WithAmount(0).Booking(), 1, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangePlan_Should_ReplaceTheInstallments_When_NothingWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(3).Build();
        var oldIds = payment.Installments.Select(i => i.Id).ToList();

        // Act
        var result = payment.ChangePlan(6, PaymentBuilder.DefaultDeadline, Now.AddHours(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        payment.InstallmentCount.Should().Be(6);
        payment.PlanRevision.Should().Be(2);
        payment.Installments.Should().HaveCount(6);
        payment.Installments.Select(i => i.Id).Should().NotIntersectWith(oldIds);
    }

    [Fact]
    public void ChangePlan_Should_ReturnPlanLocked_When_AnInstallmentWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var result = payment.ChangePlan(1, PaymentBuilder.DefaultDeadline, Now.AddHours(3));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PlanLocked);
        payment.InstallmentCount.Should().Be(3);
    }

    [Fact]
    public void ChangePlan_Should_ReturnPlanLocked_When_APaymentIsProcessing()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.Processing, PaymentMethod.Pix), Now);

        // Act
        var result = payment.ChangePlan(1, PaymentBuilder.DefaultDeadline, Now.AddHours(1));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PlanLocked);
    }

    [Fact]
    public void ChangePlan_Should_ReturnClosed_When_BookingWasCancelled()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.Reconcile(BookingStage.Closed, null, Now.AddHours(1));

        // Act
        var result = payment.ChangePlan(1, PaymentBuilder.DefaultDeadline, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Closed);
    }

    [Fact]
    public void OpenIntentIds_Should_ListOnlyChargesThatCanStillBePaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        payment.AttachIntent(2, PaymentBuilder.IntentId(2), Now.AddHours(2));

        // Act
        var open = payment.OpenIntentIds;

        // Assert
        open.Should().Equal(PaymentBuilder.IntentId(2));
    }

    [Fact]
    public void EnsurePayable_Should_ReturnTheFirstInstallment_When_NothingWasPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        var result = payment.EnsurePayable(1, Now);

        // Assert
        result.Value!.Number.Should().Be(1);
    }

    [Fact]
    public void EnsurePayable_Should_AllowPayingTheNextInstallmentEarly_When_TheFirstIsPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var result = payment.EnsurePayable(2, Now.AddDays(1));

        // Assert
        result.Value!.Number.Should().Be(2);
    }

    [Fact]
    public void EnsurePayable_Should_ReturnOutOfOrder_When_AnEarlierInstallmentIsOpen()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        var result = payment.EnsurePayable(2, Now);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentOutOfOrder(1));
    }

    [Fact]
    public void EnsurePayable_Should_ReturnAlreadyPaid_When_InstallmentIsPaid()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);

        // Act
        var result = payment.EnsurePayable(1, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentAlreadyPaid(1));
    }

    [Fact]
    public void EnsurePayable_Should_ReturnProcessing_When_StripeIsConfirmingThePayment()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.AttachIntent(1, PaymentBuilder.IntentId(1), Now);
        payment.ApplyCharge(1, new ChargeSnapshot(ChargeState.Processing, PaymentMethod.Pix), Now);

        // Act
        var result = payment.EnsurePayable(1, Now.AddMinutes(1));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentProcessing(1));
    }

    [Fact]
    public void EnsurePayable_Should_ReturnWindowClosed_When_FirstInstallmentIsPastTheDeadline()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        var result = payment.EnsurePayable(1, PaymentBuilder.DefaultDeadline.AddSeconds(1));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.PaymentWindowClosed);
    }

    [Fact]
    public void EnsurePayable_Should_ReturnInstallmentNotFound_When_NumberIsBeyondThePlan()
    {
        // Arrange
        var payment = new PaymentBuilder().WithInstallments(2).Build();

        // Act
        var result = payment.EnsurePayable(3, Now);

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.InstallmentNotFound(3));
    }

    [Fact]
    public void EnsurePayable_Should_ReturnClosed_When_BookingWasCancelled()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        payment.Reconcile(BookingStage.Cancelled, null, Now.AddHours(1));

        // Act
        var result = payment.EnsurePayable(1, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(PaymentsErrors.Closed);
    }

    [Fact]
    public void PixExpiresAt_Should_StopAtTheDeadline_When_FirstInstallmentIsDueSoon()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        var now = PaymentBuilder.DefaultDeadline.AddHours(-2);

        // Act
        var expiresAt = payment.PixExpiresAt(1, beforeCheckIn: true, now);

        // Assert
        expiresAt.Should().Be(PaymentBuilder.DefaultDeadline);
    }

    [Fact]
    public void PixExpiresAt_Should_StopAtTheEndOfTheGrace_When_LaterInstallmentIsPaidBeforeCheckIn()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var dueAt = payment.FindInstallment(2)!.DueAt;
        var now = dueAt.AddHours(20);

        // Act
        var expiresAt = payment.PixExpiresAt(2, beforeCheckIn: true, now);

        // Assert
        expiresAt.Should().Be(dueAt + Payment.OverdueGrace);
    }

    [Fact]
    public void PixExpiresAt_Should_Last24Hours_When_LaterInstallmentIsPaidAfterCheckIn()
    {
        // Arrange
        var payment = new PaymentBuilder().BuildPaid(1);
        var now = payment.FindInstallment(2)!.DueAt.AddDays(5);

        // Act
        var expiresAt = payment.PixExpiresAt(2, beforeCheckIn: false, now);

        // Assert
        expiresAt.Should().Be(now + Payment.PixLifetime);
    }

    [Fact]
    public void PixExpiresAt_Should_LastAtLeastOneMinute_When_DeadlineIsAlmostOver()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();
        var now = PaymentBuilder.DefaultDeadline.AddSeconds(-10);

        // Act
        var expiresAt = payment.PixExpiresAt(1, beforeCheckIn: true, now);

        // Assert
        expiresAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void AttachIntent_Should_CountTheAttempt_When_AChargeIsCreated()
    {
        // Arrange
        var payment = new PaymentBuilder().Build();

        // Act
        payment.AttachIntent(1, "pi_first", Now);
        payment.AttachIntent(1, "pi_second", Now.AddMinutes(5));

        // Assert
        var installment = payment.FindInstallment(1)!;
        installment.StripePaymentIntentId.Should().Be("pi_second");
        installment.PaymentAttempts.Should().Be(2);
        installment.IntentOpen.Should().BeTrue();
        payment.FindByIntent("pi_second").Should().BeSameAs(installment);
    }

    [Theory]
    [InlineData(30_000, 100, 30_000)]
    [InlineData(30_000, 50, 15_000)]
    [InlineData(10_001, 50, 5_001)]
    [InlineData(10_000, 0, 0)]
    public void RefundFor_Should_ApplyThePercentageRoundingHalfUp(long amount, int percentage, long expected)
    {
        // Act
        var refund = Payment.RefundFor(amount, percentage);

        // Assert
        refund.Should().Be(expected);
    }

    [Theory]
    [InlineData(10_000, 9_000)]
    [InlineData(5_001, 4_500)]
    [InlineData(1, 0)]
    public void HostShare_Should_BeNinetyPercentRoundedDown(long retained, long expected)
    {
        // Act
        var share = Payment.HostShare(retained);

        // Assert
        share.Should().Be(expected);
    }

    [Theory]
    [InlineData("300.00", 30_000)]
    [InlineData("121.005", 12_101)]
    [InlineData("60.50", 6_050)]
    public void ToCents_Should_ConvertReaisRoundingHalfUp(string reais, long expected)
    {
        // Act
        var cents = PayableBooking.ToCents(decimal.Parse(reais, System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        cents.Should().Be(expected);
    }
}
