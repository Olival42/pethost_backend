using FluentAssertions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.Domain.UnitTests.Payments;

/// <summary>Ganhos do anfitrião no painel: pela data do repasse em Brasília, com a composição, o guardado e o previsto.</summary>
public sealed class HostEarningsReportTests
{
    private static readonly DateTimeOffset Now = PaymentBuilder.DefaultNow;
    private static readonly DateOnly October = new(2026, 10, 1);

    private static Payment PaidInFull(long amountCents) =>
        new PaymentBuilder().WithInstallments(1).WithAmount(amountCents).ForBooking(Guid.CreateVersion7()).BuildPaid(1);

    [Fact]
    public void For_Should_CountByBrasiliaMonth_When_TransfersAreAroundMidnight()
    {
        // Arrange: 1º/10 às 02:00 UTC ainda é 30/09 em Brasília.
        var september = PaidInFull(30_000);
        september.RecordTransfer(1, "tr_set", 27_000, 3_000, new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero));
        var october = PaidInFull(20_000);
        october.RecordTransfer(1, "tr_out", 18_000, 2_000, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        // Act
        var report = HostEarningsReport.For([september, october], October);

        // Assert
        report.Transferred.Should().Be(18_000);
        report.PreviousMonthTransferred.Should().Be(27_000);
        report.YearToDateTransferred.Should().Be(45_000);
        report.PaidByOwners.Should().Be(20_000);
        report.PlatformFee.Should().Be(2_000);
        report.Refunded.Should().Be(0);
    }

    [Fact]
    public void For_Should_ExplainTheTransferAndCountTheReversalInItsOwnMonth_When_RefundedAndThenDisputeLost()
    {
        // Arrange: metade devolvida ao tutor, 90% do resto repassado em outubro e estornado em novembro.
        var payment = PaidInFull(30_000);
        payment.RecordRefund(1, "re_1", 15_000, RefundStatus.Succeeded, Now.AddDays(1));
        payment.RecordTransfer(1, "tr_1", 13_500, 1_500, Now.AddDays(2));
        payment.RecordReversal(1, "trr_1", 13_500, new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

        // Act
        var october = HostEarningsReport.For([payment], October);
        var november = HostEarningsReport.For([payment], new DateOnly(2026, 11, 20));

        // Assert
        october.Transferred.Should().Be(13_500);
        october.Reversed.Should().Be(0);
        (october.PaidByOwners - october.Refunded - october.PlatformFee).Should().Be(october.Transferred);
        october.Refunded.Should().Be(15_000);
        november.Transferred.Should().Be(0);
        november.Reversed.Should().Be(13_500);
        november.PreviousMonthTransferred.Should().Be(13_500);
        november.YearToDateTransferred.Should().Be(13_500);
    }

    [Fact]
    public void For_Should_SplitTheHeldByReason_AndForecastTheUnpaidInstallments()
    {
        // Arrange
        var awaiting = new PaymentBuilder().ForBooking(Guid.CreateVersion7()).BuildPaid(1); // 1 de 3 parcelas de R$ 100 paga
        var blocked = PaidInFull(30_000);
        blocked.RecordTransferFailed(1, "HOST_PAYOUTS_DISABLED", Now.AddDays(5));
        var disputed = PaidInFull(20_000);
        disputed.ApplyCharge(1, PaymentBuilder.Succeeded(1) with { Dispute = new DisputeSnapshot("dp_1", DisputeStatus.Open) }, Now.AddDays(3));

        // Act
        var report = HostEarningsReport.For([awaiting, blocked, disputed], October);

        // Assert
        report.AwaitingCheckOut.Should().Be(9_000);
        report.Blocked.Should().Be(27_000);
        report.Disputed.Should().Be(18_000);
        report.Expected.Should().Be(18_000, "two installments of R$ 100 are still to be paid");
        report.ExpectedInstallments.Should().Be(2);
        report.Transferred.Should().Be(0);
    }

    [Fact]
    public void For_Should_ReturnZeros_When_TheHostHasNoPayments()
    {
        // Act
        var report = HostEarningsReport.For([], October);

        // Assert
        report.Should().Be(new HostEarningsReport(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }
}
