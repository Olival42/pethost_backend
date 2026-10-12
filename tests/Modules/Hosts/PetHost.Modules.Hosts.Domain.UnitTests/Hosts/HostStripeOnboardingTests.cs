using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

/// <summary>Cadastro de recebimento no Stripe: conta conectada, espelho e quando os cantinhos aparecem.</summary>
public sealed class HostStripeOnboardingTests
{
    private const string AccountId = "acct_1PetHostTest";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly StripeAccountSnapshot Incomplete = new(false, false, false, "requirements.past_due");
    private static readonly StripeAccountSnapshot InReview = new(true, false, false, "requirements.pending_verification");
    private static readonly StripeAccountSnapshot Enabled = new(true, true, true, null);

    private static Host NewHost() =>
        Host.CreateIndividual(Guid.CreateVersion7(), Cpf.Create("52998224725").Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static Host Linked()
    {
        var host = NewHost();
        host.LinkStripeAccount(AccountId, Now);
        return host;
    }

    [Fact]
    public void EnsureCanStartStripeOnboarding_Should_Succeed_When_HostIsActive()
    {
        // Act
        var result = NewHost().EnsureCanStartStripeOnboarding();

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureCanStartStripeOnboarding_Should_Fail_When_HostIsDeactivated()
    {
        // Arrange
        var host = NewHost();
        host.Deactivate(Now);

        // Act
        var result = host.EnsureCanStartStripeOnboarding();

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeOnboardingNotAllowed);
    }

    [Fact]
    public void EnsureCanStartStripeOnboarding_Should_Fail_When_HostIsSuspended()
    {
        // Arrange
        var host = NewHost();
        host.Suspend(Now);

        // Act
        var result = host.EnsureCanStartStripeOnboarding();

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeOnboardingNotAllowed);
    }

    [Fact]
    public void LinkStripeAccount_Should_DoNothing_When_SameAccountIsLinkedAgain()
    {
        // Arrange
        var host = Linked();

        // Act
        var result = host.LinkStripeAccount(AccountId, Now.AddHours(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        host.StripeAccountId.Should().Be(AccountId);
        host.UpdatedAt.Should().Be(Now, "nada mudou");
    }

    [Fact]
    public void LinkStripeAccount_Should_Fail_When_AnotherAccountIsAlreadyLinked()
    {
        // Arrange
        var host = Linked();

        // Act
        var result = host.LinkStripeAccount("acct_1Outra", Now);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountAlreadyLinked);
        host.StripeAccountId.Should().Be(AccountId);
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_Throw_When_NoAccountIsLinked()
    {
        // Act
        var act = () => NewHost().ApplyStripeSnapshot(Enabled, Now);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_EnablePayoutsAndRecordOnboarding_When_StripeEnablesTheAccount()
    {
        // Arrange
        var host = Linked();
        var later = Now.AddDays(2);

        // Act
        var change = host.ApplyStripeSnapshot(Enabled, later);

        // Assert
        change.Should().Be(StripePayoutsChange.Enabled);
        host.StripeDetailsSubmitted.Should().BeTrue();
        host.StripeChargesEnabled.Should().BeTrue();
        host.StripePayoutsEnabled.Should().BeTrue();
        host.StripeDisabledReason.Should().BeNull();
        host.StripeOnboardedAt.Should().Be(later);
        host.StripeSyncedAt.Should().Be(later);
        host.UpdatedAt.Should().Be(later);
        host.IsListable.Should().BeTrue();
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_KeepTheFirstOnboardingDate_When_PayoutsAreEnabledAgain()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Enabled, Now);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, true, false, "requirements.past_due"), Now.AddDays(1));

        // Act
        var change = host.ApplyStripeSnapshot(Enabled, Now.AddDays(2));

        // Assert
        change.Should().Be(StripePayoutsChange.Enabled);
        host.StripeOnboardedAt.Should().Be(Now, "é a primeira vez que o Stripe liberou");
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_ReportDisabled_When_StripeBlocksPayouts()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Enabled, Now);

        // Act
        var change = host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, false, false, "requirements.past_due"), Now.AddDays(1));

        // Assert
        change.Should().Be(StripePayoutsChange.Disabled);
        host.StripePayoutsEnabled.Should().BeFalse();
        host.StripeDisabledReason.Should().Be("requirements.past_due");
        host.IsListable.Should().BeFalse();
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_OnlyTouchSyncDate_When_NothingChanged()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Incomplete, Now);
        var later = Now.AddHours(3);

        // Act
        var change = host.ApplyStripeSnapshot(Incomplete, later);

        // Assert
        change.Should().Be(StripePayoutsChange.None);
        host.StripeSyncedAt.Should().Be(later);
        host.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void ApplyStripeSnapshot_Should_NormalizeTheDisabledReason_When_ItIsBlankOrTooLong()
    {
        // Arrange
        var host = Linked();

        // Act
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(false, false, false, "   "), Now);
        var blank = host.StripeDisabledReason;
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(false, false, false, " " + new string('x', 150)), Now);

        // Assert
        blank.Should().BeNull();
        host.StripeDisabledReason.Should().HaveLength(StripeAccountSnapshot.DisabledReasonMaxLength);
    }

    [Fact]
    public void StripeOnboardingStatus_Should_BeNotStarted_When_ThereIsNoAccount()
    {
        NewHost().StripeOnboardingStatus.Should().Be(StripeOnboardingStatus.NotStarted);
    }

    [Theory]
    [InlineData(false, false, "requirements.past_due", StripeOnboardingStatus.InProgress)]
    [InlineData(true, false, "requirements.pending_verification", StripeOnboardingStatus.UnderReview)]
    [InlineData(true, false, "under_review", StripeOnboardingStatus.UnderReview)]
    [InlineData(true, false, null, StripeOnboardingStatus.UnderReview)]
    [InlineData(true, false, "requirements.past_due", StripeOnboardingStatus.Restricted)]
    [InlineData(true, false, "rejected.fraud", StripeOnboardingStatus.Rejected)]
    [InlineData(false, false, "rejected.terms_of_service", StripeOnboardingStatus.Rejected)]
    [InlineData(true, true, null, StripeOnboardingStatus.Enabled)]
    public void StripeOnboardingStatus_Should_FollowTheStripeAccount_When_AccountIsLinked(
        bool detailsSubmitted,
        bool payoutsEnabled,
        string? disabledReason,
        StripeOnboardingStatus expected)
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(detailsSubmitted, payoutsEnabled, payoutsEnabled, disabledReason), Now);

        // Act / Assert
        host.StripeOnboardingStatus.Should().Be(expected);
    }

    [Fact]
    public void IsListable_Should_BeFalse_When_PayoutsAreEnabledButHostIsSuspended()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(Enabled, Now);

        // Act
        host.Suspend(Now);

        // Assert
        host.IsListable.Should().BeFalse();
    }

    [Fact]
    public void IsListable_Should_BeFalse_When_StripeIsStillReviewing()
    {
        // Arrange
        var host = Linked();

        // Act
        host.ApplyStripeSnapshot(InReview, Now);

        // Assert
        host.IsListable.Should().BeFalse("o cantinho só aparece depois que o Stripe libera os repasses");
    }

    [Theory]
    [InlineData(StripeOnboardingStatus.NotStarted, "not_started")]
    [InlineData(StripeOnboardingStatus.InProgress, "in_progress")]
    [InlineData(StripeOnboardingStatus.UnderReview, "under_review")]
    [InlineData(StripeOnboardingStatus.Restricted, "restricted")]
    [InlineData(StripeOnboardingStatus.Rejected, "rejected")]
    [InlineData(StripeOnboardingStatus.Enabled, "enabled")]
    public void StripeOnboardingStatusValues_Should_MapEveryStatus_When_ConvertedToWire(StripeOnboardingStatus status, string expected)
    {
        StripeOnboardingStatusValues.ToWire(status).Should().Be(expected);
    }

    [Theory]
    [InlineData("in_progress", StripeOnboardingStatus.InProgress)]
    [InlineData("REJECTED", StripeOnboardingStatus.Rejected)]
    [InlineData(" enabled ", StripeOnboardingStatus.Enabled)]
    public void StripeOnboardingStatusValues_Should_ReadTheWireValue_When_ItIsKnown(string value, StripeOnboardingStatus expected)
    {
        StripeOnboardingStatusValues.TryFromWire(value, out var status).Should().BeTrue();
        status.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aprovado")]
    public void StripeOnboardingStatusValues_Should_RefuseTheWireValue_When_ItIsUnknown(string? value)
    {
        StripeOnboardingStatusValues.TryFromWire(value, out _).Should().BeFalse();
    }

    // ----- lembretes -----

    private static readonly TimeSpan[] Schedule = [TimeSpan.FromDays(3), TimeSpan.FromDays(7)];

    [Fact]
    public void LinkStripeAccount_Should_RecordWhenTheSetupWasOpened_When_FirstLinked()
    {
        // Act
        var host = Linked();

        // Assert
        host.StripeAccountCreatedAt.Should().Be(Now);
    }

    [Fact]
    public void IsStripeReminderDue_Should_CountFromTheApproval_When_TheHostNeverStarted()
    {
        // Arrange: aprovado em Now, nunca abriu o cadastro.
        var host = NewHost();

        // Act / Assert
        host.IsStripeReminderDue(Now.AddDays(2), Schedule).Should().BeFalse();
        host.IsStripeReminderDue(Now.AddDays(3), Schedule).Should().BeTrue();
    }

    [Fact]
    public void IsStripeReminderDue_Should_FollowTheSchedule_When_RemindersWereSent()
    {
        // Arrange
        var host = Linked();
        host.RecordStripeReminder(Now.AddDays(3));

        // Act / Assert
        host.StripeRemindersSent.Should().Be(1);
        host.StripeRemindedAt.Should().Be(Now.AddDays(3));
        host.IsStripeReminderDue(Now.AddDays(5), Schedule).Should().BeFalse("o segundo lembrete é no dia 7");
        host.IsStripeReminderDue(Now.AddDays(7), Schedule).Should().BeTrue();

        host.RecordStripeReminder(Now.AddDays(7));
        host.IsStripeReminderDue(Now.AddDays(30), Schedule).Should().BeFalse("os lembretes acabaram");
    }

    [Fact]
    public void IsStripeReminderDue_Should_RemindRestrictedAccounts_When_StripeAsksForSomething()
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, false, false, "requirements.past_due"), Now);

        // Act / Assert
        host.IsStripeReminderDue(Now.AddDays(3), Schedule).Should().BeTrue();
    }

    [Theory]
    [InlineData("requirements.pending_verification", false)]
    [InlineData("rejected.fraud", false)]
    [InlineData(null, true)]
    public void IsStripeReminderDue_Should_NotRemind_When_StripeIsReviewingRejectedOrEnabled(string? reason, bool payoutsEnabled)
    {
        // Arrange
        var host = Linked();
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, payoutsEnabled, payoutsEnabled, reason), Now);

        // Act / Assert
        host.IsStripeReminderDue(Now.AddDays(30), Schedule).Should().BeFalse();
    }

    [Fact]
    public void IsStripeReminderDue_Should_NotRemind_When_HostIsSuspendedOrDeactivated()
    {
        // Arrange
        var suspended = NewHost();
        suspended.Suspend(Now);
        var deactivated = NewHost();
        deactivated.Deactivate(Now);

        // Act / Assert
        suspended.IsStripeReminderDue(Now.AddDays(30), Schedule).Should().BeFalse();
        deactivated.IsStripeReminderDue(Now.AddDays(30), Schedule).Should().BeFalse();
        NewHost().IsStripeReminderDue(Now.AddDays(30), []).Should().BeFalse("sem agenda, sem lembrete");
    }

    // ----- recusa definitiva e reset pelo admin -----

    private static Host RejectedHost()
    {
        var host = Linked();
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, false, false, "rejected.other"), Now);
        return host;
    }

    [Fact]
    public void EnsureCanStartStripeOnboarding_Should_Fail_When_StripeRejectedTheAccount()
    {
        RejectedHost().EnsureCanStartStripeOnboarding().FirstError.Should().Be(HostsErrors.StripeAccountRejected);
    }

    [Fact]
    public void ResetStripeAccount_Should_KeepTheRejectedAccountAndStartOver_When_AccountWasRejected()
    {
        // Arrange
        var host = RejectedHost();
        host.RecordStripeReminder(Now);
        var later = Now.AddDays(1);

        // Act
        var result = host.ResetStripeAccount(later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        host.StripeRejectedAccountId.Should().Be(AccountId);
        host.StripeAccountId.Should().BeNull();
        host.StripeAccountResets.Should().Be(1);
        host.StripeOnboardingStatus.Should().Be(StripeOnboardingStatus.NotStarted);
        host.StripeDisabledReason.Should().BeNull();
        host.StripeAccountCreatedAt.Should().BeNull();
        host.StripeRemindersSent.Should().Be(0);
        host.UpdatedAt.Should().Be(later);
        host.EnsureCanStartStripeOnboarding().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ResetStripeAccount_Should_Fail_When_AccountWasNotRejected()
    {
        // Arrange
        var host = Linked();

        // Act
        var result = host.ResetStripeAccount(Now);

        // Assert
        result.FirstError.Should().Be(HostsErrors.StripeAccountNotRejected);
        host.StripeAccountId.Should().Be(AccountId);
        NewHost().ResetStripeAccount(Now).FirstError.Should().Be(HostsErrors.StripeAccountNotRejected);
    }
}
