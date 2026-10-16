using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.GetHostById;
using PetHost.Modules.Hosts.Application.Hosts.ResetHostStripeAccount;
using PetHost.Modules.Hosts.Application.Hosts.RunPayoutsMaintenance;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Modules.Hosts.Application.Hosts.SyncStripeAccount;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.Hosts;

/// <summary>
/// Quando o cadastro de recebimento não anda: releitura periódica no Stripe (webhook perdido),
/// lembretes, recusa definitiva do Stripe e o filtro do admin.
/// </summary>
public sealed class PayoutsMaintenanceTests
{
    private const string AccountId = "acct_1Manutencao";

    private static readonly StripeAccountSnapshot Pending = new(false, false, false, "requirements.past_due");
    private static readonly StripeAccountSnapshot Enabled = new(true, true, true, null);
    private static readonly StripeAccountSnapshot Rejected = new(true, false, false, "rejected.fraud");

    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IHostAccounts> _accounts = Accounts();
    private readonly Mock<IStripeConnect> _stripe = new();
    private readonly Mock<IHostPayoutsNotifier> _notifier = new();
    private readonly Mock<IHostBookings> _bookings = new();
    private readonly Mock<IHostPayoutsPolicy> _policy = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(Now);

    public PayoutsMaintenanceTests()
    {
        _bookings.Setup(b => b.ListOpenBookingIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _policy.Setup(p => p.ReminderSchedule).Returns([TimeSpan.FromDays(3), TimeSpan.FromDays(7)]);
        _policy.Setup(p => p.EnabledResyncInterval).Returns(TimeSpan.FromHours(24));
        _stripe.Setup(s => s.GetAccountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Pending));
    }

    private static Host NewHost(Guid? userId = null) =>
        Host.CreateIndividual(userId ?? Guid.CreateVersion7(), AnyCpf, Guid.CreateVersion7(), AdminId, Now);

    private static Host Linked(string accountId = AccountId)
    {
        var host = NewHost();
        host.LinkStripeAccount(accountId, Now);
        return host;
    }

    private void Listed(params Host[] hosts) =>
        _hosts.Setup(h => h.ListForPayoutsMaintenanceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hosts);

    private HostPayoutSetup Setup() =>
        new(_hosts.Object, _unitOfWork.Object, _accounts.Object, _stripe.Object, _notifier.Object, _bookings.Object, _auditTrail.Object, _time);

    private RunPayoutsMaintenanceCommandHandler Maintenance() =>
        new(_hosts.Object, _unitOfWork.Object, _accounts.Object, Setup(), _notifier.Object, _policy.Object, _auditTrail.Object, _time);

    private void Recorded(string action, Times times) =>
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == action), It.IsAny<CancellationToken>()), times);

    // ----- releitura periódica -----

    [Fact]
    public async Task Maintenance_Should_EnableTheHost_When_TheWebhookWasLost()
    {
        // Arrange: o Stripe aprovou, mas o webhook nunca chegou.
        var host = Linked();
        Listed(host);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Enabled));

        // Act
        var result = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RunPayoutsMaintenanceResponse(Synced: 1, Failed: 0, Reminded: 0));
        host.IsListable.Should().BeTrue();
        Recorded(AuditActions.HostStripePayoutsEnabled, Times.Once());
    }

    [Fact]
    public async Task Maintenance_Should_StopRereading_When_StripeIsUnavailable()
    {
        // Arrange
        Listed(Linked("acct_1A"), Linked("acct_1B"), Linked("acct_1C"));
        _stripe.Setup(s => s.GetAccountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Failure(HostsErrors.StripeUnavailable));

        // Act
        var result = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value!.Failed.Should().Be(1, "não adianta bater no Stripe fora do ar para cada anfitrião");
        _stripe.Verify(s => s.GetAccountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Maintenance_Should_SkipRejectedAccountsAndHostsWithoutAccount_When_Rereading()
    {
        // Arrange
        var rejected = Linked("acct_1Recusada");
        rejected.ApplyStripeSnapshot(Rejected, Now);
        Listed(rejected, NewHost());

        // Act
        var result = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value!.Synced.Should().Be(0);
        _stripe.VerifyNoOtherCalls();
    }

    // ----- lembretes -----

    [Fact]
    public async Task Maintenance_Should_RemindOnceAndRecord_When_TheReminderIsDue()
    {
        // Arrange: abriu o cadastro há 3 dias e parou no meio.
        var host = Linked();
        Listed(host);
        _time.Advance(TimeSpan.FromDays(3));

        // Act
        var first = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);
        var second = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        first.Value!.Reminded.Should().Be(1);
        second.Value!.Reminded.Should().Be(0, "o segundo lembrete só vai no dia 7");
        host.StripeRemindersSent.Should().Be(1);
        _notifier.Verify(
            n => n.NotifyPayoutSetupReminderAsync(
                It.Is<HostPayoutsReminder>(r => r.Email == "cida@exemplo.com" && r.Status == StripeOnboardingStatusValues.InProgress && r.ReminderNumber == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Recorded(AuditActions.HostStripeReminderSent, Times.Once());
    }

    [Fact]
    public async Task Maintenance_Should_RemindApprovedHosts_When_TheyNeverStartedThePayoutSetup()
    {
        // Arrange
        Listed(NewHost());
        _time.Advance(TimeSpan.FromDays(3));

        // Act
        var result = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value!.Reminded.Should().Be(1);
        _notifier.Verify(
            n => n.NotifyPayoutSetupReminderAsync(It.Is<HostPayoutsReminder>(r => r.Status == StripeOnboardingStatusValues.NotStarted), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Maintenance_Should_NotRemind_When_SavingTheReminderFails()
    {
        // Arrange
        Listed(NewHost());
        _time.Advance(TimeSpan.FromDays(3));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(HostsErrors.ApplicationConflict));

        // Act
        var result = await Maintenance().HandleAsync(new RunPayoutsMaintenanceCommand(), CancellationToken.None);

        // Assert
        result.Value!.Reminded.Should().Be(0);
        _notifier.Verify(n => n.NotifyPayoutSetupReminderAsync(It.IsAny<HostPayoutsReminder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- recusa definitiva do Stripe -----

    [Fact]
    public async Task Sync_Should_AlertTheHostAndTheAdminOnce_When_StripeRejectsTheAccount()
    {
        // Arrange
        var host = Linked();
        _hosts.Setup(h => h.GetByStripeAccountIdAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(host);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Rejected));
        var sync = new SyncStripeAccountCommandHandler(_hosts.Object, Setup());

        // Act: o mesmo evento chega duas vezes.
        await sync.HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);
        await sync.HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);

        // Assert
        host.StripeOnboardingStatus.Should().Be(StripeOnboardingStatus.Rejected);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.HostStripeAccountRejected && r.Details!["disabledReason"] == "rejected.fraud"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.NotifyPayoutsRejectedAsync(It.Is<HostPayoutsNotice>(x => x.Email == "cida@exemplo.com"), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(
            n => n.NotifyAdminPayoutsRejectedAsync(
                It.Is<HostPayoutsRejection>(x => x.HostId == host.Id.Value && x.HostName == "Dona Cida" && x.Reason == "rejected.fraud"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Sync_Should_ReportOnlyTheRejection_When_AnEnabledAccountIsRejected()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Enabled, Now);
        _hosts.Setup(h => h.GetByStripeAccountIdAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(host);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Rejected));

        // Act
        await new SyncStripeAccountCommandHandler(_hosts.Object, Setup()).HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);

        // Assert
        host.IsListable.Should().BeFalse();
        Recorded(AuditActions.HostStripeAccountRejected, Times.Once());
        Recorded(AuditActions.HostStripePayoutsDisabled, Times.Never());
    }

    // ----- filtro do admin -----

    [Fact]
    public async Task ListHosts_Should_FilterByStripeStatus_When_Asked()
    {
        // Arrange
        var stuck = Linked("acct_1Travado");
        var enabled = Linked("acct_1Liberado");
        enabled.ApplyStripeSnapshot(Enabled, Now);
        _hosts.Setup(h => h.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([stuck, enabled, NewHost()]);
        var sut = new ListHostsQueryHandler(_hosts.Object, _accounts.Object, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new ListHostsQuery("IN_PROGRESS"), CancellationToken.None);
        var all = await sut.HandleAsync(new ListHostsQuery(), CancellationToken.None);

        // Assert
        result.Value.Should().ContainSingle().Which.Id.Should().Be(stuck.Id.Value);
        all.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListHosts_Should_Return400_When_TheStripeStatusIsUnknown()
    {
        // Act
        var result = await new ListHostsQueryHandler(_hosts.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new ListHostsQuery("aprovado"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeStatusFilterInvalid);
        _hosts.Verify(h => h.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- reset pelo admin -----

    private ResetHostStripeAccountCommandHandler Reset() =>
        new(_hosts.Object, _unitOfWork.Object, _accounts.Object, _auditTrail.Object, _time);

    [Fact]
    public async Task Reset_Should_FreeANewAttemptAndAuditWithReason_When_AccountWasRejected()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Rejected, Now);
        _hosts.Setup(h => h.GetByIdAsync(host.Id, It.IsAny<CancellationToken>())).ReturnsAsync(host);

        // Act
        var result = await Reset().HandleAsync(new ResetHostStripeAccountCommand(AdminId, host.Id.Value, " Dado digitado errado "), CancellationToken.None);

        // Assert
        result.Value!.StripeStatus.Should().Be(StripeOnboardingStatusValues.NotStarted);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.HostStripeAccountReset
                    && r.Reason == "Dado digitado errado"
                    && r.Details!["rejectedStripeAccountId"] == AccountId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reset_Should_Refuse_When_AccountWasNotRejected()
    {
        // Arrange
        var host = Linked();
        _hosts.Setup(h => h.GetByIdAsync(host.Id, It.IsAny<CancellationToken>())).ReturnsAsync(host);

        // Act
        var result = await Reset().HandleAsync(new ResetHostStripeAccountCommand(AdminId, host.Id.Value, "x"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountNotRejected);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_Should_ReturnNotFound_When_HostDoesNotExist()
    {
        // Act
        var result = await Reset().HandleAsync(new ResetHostStripeAccountCommand(AdminId, Guid.CreateVersion7(), "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_FOUND");
    }

    [Fact]
    public void ResetValidator_Should_RequireAReason_When_Empty()
    {
        new ResetHostStripeAccountCommandValidator()
            .Validate(new ResetHostStripeAccountCommand(AdminId, Guid.CreateVersion7(), " "))
            .IsValid.Should().BeFalse();
    }
}
