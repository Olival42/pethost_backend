using FluentAssertions;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.HostApplications;

/// <summary>Quando o histórico bloqueia o CPF de vez, e o próprio bloqueio.</summary>
public sealed class IdentityBlockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();
    private static readonly string Print = new('a', IdentityBlock.FingerprintLength);

    private static HostApplication Submitted(IReadOnlyCollection<HostApplication> history)
    {
        var application = HostApplication.Start(history, UserId, Cpf.Create("52998224725").Value!, IdentityDocumentType.Cnh, null, "v1", null, Now).Value!;
        application.AttachDocument(DocumentKind.Front, "k/front.jpg", new string('f', 64), 10, Now);
        application.AttachDocument(DocumentKind.Back, "k/back.jpg", new string('b', 64), 10, Now);
        application.AttachDocument(DocumentKind.Selfie, "k/selfie.jpg", new string('s', 64), 10, Now);
        application.Submit(Now);
        return application;
    }

    private static HostApplication Rejected(List<HostApplication> history, RejectionReason reason)
    {
        var application = Submitted(history);
        application.Reject(AdminId, reason, "x", Now);
        history.Add(application);
        return application;
    }

    [Fact]
    public void IdentityBlockFor_Should_ReturnFraud_When_ARejectionWasForSuspectedFraud()
    {
        var history = new List<HostApplication>();
        Rejected(history, RejectionReason.SuspectedFraud);

        HostApplication.IdentityBlockFor(history).Should().Be(IdentityBlockReason.Fraud);
    }

    [Fact]
    public void IdentityBlockFor_Should_ReturnAttemptsExhausted_When_TheLastAttemptWasRejected()
    {
        var history = new List<HostApplication>();
        for (var i = 0; i < HostApplication.MaxAttempts; i++)
            Rejected(history, RejectionReason.DocumentIllegible);

        HostApplication.IdentityBlockFor(history).Should().Be(IdentityBlockReason.AttemptsExhausted);
    }

    [Fact]
    public void IdentityBlockFor_Should_ReturnNull_When_ThePersonCanStillTry()
    {
        var history = new List<HostApplication>();
        for (var i = 0; i < HostApplication.MaxAttempts - 1; i++)
            Rejected(history, RejectionReason.DocumentIllegible);

        HostApplication.IdentityBlockFor(history).Should().BeNull();
    }

    [Fact]
    public void Create_Should_KeepTheFingerprintReasonAndApplication_When_Valid()
    {
        var applicationId = Guid.CreateVersion7();

        var block = IdentityBlock.Create(Print, IdentityBlockReason.AttemptsExhausted, applicationId, Now);

        block.Id.Value.Should().NotBe(Guid.Empty);
        block.CpfFingerprint.Should().Be(Print);
        block.Reason.Should().Be(IdentityBlockReason.AttemptsExhausted);
        block.ApplicationId.Should().Be(applicationId);
        block.BlockedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("curto")]
    public void Create_Should_Throw_When_TheFingerprintHasTheWrongSize(string print)
    {
        var act = () => IdentityBlock.Create(print, IdentityBlockReason.Fraud, Guid.CreateVersion7(), Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(IdentityBlockReason.Fraud, "fraud")]
    [InlineData(IdentityBlockReason.AttemptsExhausted, "attempts_exhausted")]
    public void ReasonValues_Should_RoundTrip_When_Converted(IdentityBlockReason reason, string wire)
    {
        IdentityBlockReasonValues.ToWire(reason).Should().Be(wire);
        IdentityBlockReasonValues.FromWire(wire).Should().Be(reason);
    }
}
