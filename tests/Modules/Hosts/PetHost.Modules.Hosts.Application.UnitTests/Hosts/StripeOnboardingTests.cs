using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.CreateMyStripeOnboardingLink;
using PetHost.Modules.Hosts.Application.Hosts.GetMyStripeAccount;
using PetHost.Modules.Hosts.Application.Hosts.StartMyStripeOnboarding;
using PetHost.Modules.Hosts.Application.Hosts.StripeOnboarding;
using PetHost.Modules.Hosts.Application.Hosts.SyncMyStripeAccount;
using PetHost.Modules.Hosts.Application.Hosts.SyncStripeAccount;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.Hosts;

/// <summary>Cadastro de recebimento no Stripe: só depois da aprovação; cantinho só com repasses liberados.</summary>
public sealed class StripeOnboardingTests
{
    private const string AccountId = "acct_1NovaConta";

    private static readonly StripeAccountSnapshot Pending = new(false, false, false, "requirements.past_due");
    private static readonly StripeAccountSnapshot Enabled = new(true, true, true, null);

    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IHostAccounts> _accounts = Accounts();
    private readonly Mock<IStripeConnect> _stripe = new();
    private readonly Mock<IHostPayoutsNotifier> _notifier = new();
    private readonly Mock<IHostBookings> _bookings = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(Now.AddDays(1));
    private readonly Host _individual = Host.CreateIndividual(UserId, AnyCpf, Guid.CreateVersion7(), AdminId, Now);
    private readonly Host _company = Host.CreateCompany(
        UserId,
        AnyCpf,
        Cnpj.Create("11222333000181").Value!,
        "Pet Feliz Hospedagem LTDA",
        "Pet Feliz",
        CompanyAddress.Create("87013000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!,
        Guid.CreateVersion7(),
        AdminId,
        Now).Value!;

    public StripeOnboardingTests()
    {
        _bookings.Setup(b => b.ListOpenBookingIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _stripe.Setup(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success(AccountId));
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Pending));
        _stripe.Setup(s => s.CreateOnboardingSessionAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeOnboardingSession>.Success(new StripeOnboardingSession("accs_secret", "pk_test_x", Now.AddMinutes(30))));
        _stripe.Setup(s => s.CreateOnboardingLinkAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeOnboardingLink>.Success(new StripeOnboardingLink("https://connect.stripe.com/setup/x", Now.AddMinutes(5))));
    }

    private void Is(Host host)
    {
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(host);
        _hosts.Setup(h => h.GetByStripeAccountIdAsync(AccountId, It.IsAny<CancellationToken>())).ReturnsAsync(host);
    }

    private HostPayoutSetup Setup() =>
        new(_hosts.Object, _unitOfWork.Object, _accounts.Object, _stripe.Object, _notifier.Object, _bookings.Object, _auditTrail.Object, _time);

    private StartMyStripeOnboardingCommandHandler Start() => new(Setup(), _stripe.Object);

    private CreateMyStripeOnboardingLinkCommandHandler Link() => new(Setup(), _stripe.Object);

    private SyncMyStripeAccountCommandHandler SyncMine() => new(_hosts.Object, Setup());

    private SyncStripeAccountCommandHandler SyncFromWebhook() => new(_hosts.Object, Setup());

    private void Recorded(string action, Times times) =>
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == action), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Start_Should_ReturnHostNotFound_When_AccountWasNotApprovedYet()
    {
        // Arrange: sem perfil de anfitrião = o admin ainda não aprovou a verificação.

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.ProfileNotFound);
        _stripe.Verify(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_Should_RefuseWithoutCallingStripe_When_HostIsSuspended()
    {
        // Arrange
        _individual.Suspend(Now);
        Is(_individual);

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeOnboardingNotAllowed);
        _stripe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_Should_CreatePrefilledAccountAndSession_When_FirstTime()
    {
        // Arrange
        Is(_individual);
        StripeAccountDraft? sent = null;
        _stripe.Setup(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()))
            .Callback<StripeAccountDraft, CancellationToken>((draft, _) => sent = draft)
            .ReturnsAsync(Result<string>.Success(AccountId));

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ClientSecret.Should().Be("accs_secret");
        result.Value.PublishableKey.Should().Be("pk_test_x");
        result.Value.ExpiresAt.Should().Be(Now.AddMinutes(30).ToUnixTimeSeconds());
        result.Value.Account.Status.Should().Be(StripeOnboardingStatusValues.InProgress);
        result.Value.Account.ListingsVisible.Should().BeFalse();

        _individual.StripeAccountId.Should().Be(AccountId);
        sent.Should().NotBeNull();
        sent!.HostId.Should().Be(_individual.Id.Value);
        sent.PersonType.Should().Be(PersonType.Individual);
        sent.Cpf.Should().Be(AnyCpf.Value);
        sent.FullName.Should().Be("Dona Cida");
        sent.Email.Should().Be("cida@exemplo.com");
        sent.BirthDate.Should().Be(new DateOnly(1968, 3, 15));
        sent.Cnpj.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Recorded(AuditActions.HostStripeAccountCreated, Times.Once());
    }

    [Fact]
    public async Task Start_Should_SendCompanyData_When_HostIsACompany()
    {
        // Arrange
        Is(_company);
        StripeAccountDraft? sent = null;
        _stripe.Setup(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()))
            .Callback<StripeAccountDraft, CancellationToken>((draft, _) => sent = draft)
            .ReturnsAsync(Result<string>.Success(AccountId));

        // Act
        await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        sent!.PersonType.Should().Be(PersonType.Company);
        sent.Cnpj.Should().Be("11222333000181");
        sent.LegalName.Should().Be("Pet Feliz Hospedagem LTDA");
        sent.TradeName.Should().Be("Pet Feliz");
        sent.CompanyAddress!.ZipCode.Should().Be("87013000");
        sent.CompanyAddress.City.Should().Be("Maringá");
        sent.Cpf.Should().Be(AnyCpf.Value, "na empresa o CPF é o do representante legal");
    }

    [Fact]
    public async Task Start_Should_ReuseTheAccount_When_OnboardingWasAlreadyStarted()
    {
        // Arrange
        _individual.LinkStripeAccount(AccountId, Now);
        Is(_individual);

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _stripe.Verify(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Recorded(AuditActions.HostStripeAccountCreated, Times.Never());
    }

    [Fact]
    public async Task Start_Should_ReturnStripeError_When_StripeIsUnavailable()
    {
        // Arrange
        Is(_individual);
        _stripe.Setup(s => s.CreateAccountAsync(It.IsAny<StripeAccountDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Failure(HostsErrors.StripeUnavailable));

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeUnavailable);
        _individual.StripeAccountId.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_Should_NotOpenASession_When_SavingTheAccountFails()
    {
        // Arrange
        Is(_individual);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(HostsErrors.StripeAccountAlreadyLinked));

        // Act
        var result = await Start().HandleAsync(new StartMyStripeOnboardingCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountAlreadyLinked);
        _stripe.Verify(s => s.CreateOnboardingSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Recorded(AuditActions.HostStripeAccountCreated, Times.Never());
    }

    [Fact]
    public async Task Link_Should_ReturnHostedLink_When_HostIsApproved()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await Link().HandleAsync(new CreateMyStripeOnboardingLinkCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().Be("https://connect.stripe.com/setup/x");
        result.Value.ExpiresAt.Should().Be(Now.AddMinutes(5).ToUnixTimeSeconds());
        _individual.StripeAccountId.Should().Be(AccountId);
    }

    [Fact]
    public async Task Link_Should_ReturnHostNotFound_When_AccountWasNotApprovedYet()
    {
        // Act
        var result = await Link().HandleAsync(new CreateMyStripeOnboardingLinkCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.ProfileNotFound);
    }

    [Fact]
    public async Task GetMine_Should_ReturnNotStarted_When_ThereIsNoAccount()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await new GetMyStripeAccountQueryHandler(_hosts.Object)
            .HandleAsync(new GetMyStripeAccountQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be(StripeOnboardingStatusValues.NotStarted);
        result.Value.PayoutsEnabled.Should().BeFalse();
        _stripe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMine_Should_ReturnHostNotFound_When_AccountWasNotApprovedYet()
    {
        // Act
        var result = await new GetMyStripeAccountQueryHandler(_hosts.Object)
            .HandleAsync(new GetMyStripeAccountQuery(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.ProfileNotFound);
    }

    [Fact]
    public async Task SyncMine_Should_ReturnAccountNotFound_When_OnboardingWasNotStarted()
    {
        // Arrange
        Is(_individual);

        // Act
        var result = await SyncMine().HandleAsync(new SyncMyStripeAccountCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountNotFound);
    }

    [Fact]
    public async Task SyncMine_Should_EnableListingsNotifyAndAudit_When_StripeEnablesPayouts()
    {
        // Arrange
        _individual.LinkStripeAccount(AccountId, Now);
        Is(_individual);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Enabled));

        // Act
        var result = await SyncMine().HandleAsync(new SyncMyStripeAccountCommand(UserId), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be(StripeOnboardingStatusValues.Enabled);
        result.Value.ListingsVisible.Should().BeTrue();
        result.Value.OnboardedAt.Should().Be(Now.AddDays(1));
        Recorded(AuditActions.HostStripePayoutsEnabled, Times.Once());
        _notifier.Verify(
            n => n.NotifyPayoutsEnabledAsync(It.Is<HostPayoutsNotice>(x => x.Email == "cida@exemplo.com"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncMine_Should_NotNotifyAgain_When_PayoutsWereAlreadyEnabled()
    {
        // Arrange
        _individual.LinkStripeAccount(AccountId, Now);
        _individual.ApplyStripeSnapshot(Enabled, Now);
        Is(_individual);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(Enabled));

        // Act
        await SyncMine().HandleAsync(new SyncMyStripeAccountCommand(UserId), CancellationToken.None);

        // Assert
        _notifier.VerifyNoOtherCalls();
        Recorded(AuditActions.HostStripePayoutsEnabled, Times.Never());
    }

    [Fact]
    public async Task SyncFromWebhook_Should_AuditTheBlock_When_StripeDisablesPayouts()
    {
        // Arrange
        _individual.LinkStripeAccount(AccountId, Now);
        _individual.ApplyStripeSnapshot(Enabled, Now);
        Is(_individual);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(new StripeAccountSnapshot(true, false, false, "requirements.past_due")));

        // Act
        var result = await SyncFromWebhook().HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _individual.IsListable.Should().BeFalse();
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.HostStripePayoutsDisabled && r.Details!["disabledReason"] == "requirements.past_due"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncFromWebhook_Should_AlertTheAdminWithTheOpenBookings_When_StripeBlocksAHostWithBookings()
    {
        // Arrange
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        _individual.LinkStripeAccount(AccountId, Now);
        _individual.ApplyStripeSnapshot(Enabled, Now);
        Is(_individual);
        _bookings.Setup(b => b.ListOpenBookingIdsAsync(_individual.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Success(new StripeAccountSnapshot(true, false, false, "requirements.past_due")));

        // Act
        await SyncFromWebhook().HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);
        await SyncFromWebhook().HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);

        // Assert — uma vez só: o segundo aviso do Stripe não muda nada.
        _notifier.Verify(
            n => n.NotifyAdminPayoutsBlockedAsync(
                It.Is<HostPayoutsBlocked>(b => b.HostId == _individual.Id.Value
                    && b.Reason == "requirements.past_due"
                    && b.OpenBookingIds.SequenceEqual(new[] { first, second })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncFromWebhook_Should_ReturnAccountNotFound_When_AccountIsNotFromAnyHost()
    {
        // Act
        var result = await SyncFromWebhook().HandleAsync(new SyncStripeAccountCommand("acct_1Desconhecida"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountNotFound);
        _stripe.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncFromWebhook_Should_KeepTheMirror_When_StripeIsUnavailable()
    {
        // Arrange
        _individual.LinkStripeAccount(AccountId, Now);
        Is(_individual);
        _stripe.Setup(s => s.GetAccountAsync(AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StripeAccountSnapshot>.Failure(HostsErrors.StripeUnavailable));

        // Act
        var result = await SyncFromWebhook().HandleAsync(new SyncStripeAccountCommand(AccountId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeUnavailable);
        _individual.StripeSyncedAt.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validators_Should_RequireIds_When_Empty()
    {
        new StartMyStripeOnboardingCommandValidator().Validate(new StartMyStripeOnboardingCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new CreateMyStripeOnboardingLinkCommandValidator().Validate(new CreateMyStripeOnboardingLinkCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new SyncMyStripeAccountCommandValidator().Validate(new SyncMyStripeAccountCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new SyncStripeAccountCommandValidator().Validate(new SyncStripeAccountCommand(" ")).IsValid.Should().BeFalse();
    }
}
