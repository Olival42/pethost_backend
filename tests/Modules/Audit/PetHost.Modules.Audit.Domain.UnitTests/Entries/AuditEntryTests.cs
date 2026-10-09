using FluentAssertions;
using PetHost.Modules.Audit.Domain.Entries;
using Xunit;

namespace PetHost.Modules.Audit.Domain.UnitTests.Entries;

public sealed class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Should_KeepEveryField_When_ValuesAreValid()
    {
        var target = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        var entry = AuditEntry.Create(
            Now, "owner.suspended", "owner", target, actor, "admin", "fraude de CPF",
            new Dictionary<string, string?> { ["cpf"] = "***.***.247-25" }, "203.0.113.7", "trace-1");

        entry.Id.Value.Should().NotBe(Guid.Empty);
        entry.OccurredAt.Should().Be(Now);
        entry.Action.Should().Be("owner.suspended");
        entry.TargetType.Should().Be("owner");
        entry.TargetId.Should().Be(target);
        entry.ActorId.Should().Be(actor);
        entry.ActorRole.Should().Be("admin");
        entry.Reason.Should().Be("fraude de CPF");
        entry.Details.Should().ContainKey("cpf").WhoseValue.Should().Be("***.***.247-25");
        entry.IpAddress.Should().Be("203.0.113.7");
        entry.TraceId.Should().Be("trace-1");
    }

    [Fact]
    public void Create_Should_CutLongTexts_When_TheyPassTheLimits()
    {
        // A trilha não pode perder um fato por causa do tamanho de um campo.
        var entry = AuditEntry.Create(
            Now, "account.suspended", "account", null, null, null, new string('x', 900),
            new Dictionary<string, string?> { ["note"] = new string('y', 400) }, null, null);

        entry.Reason.Should().HaveLength(AuditEntry.ReasonMaxLength);
        entry.Details["note"].Should().HaveLength(AuditEntry.DetailValueMaxLength);
    }

    [Fact]
    public void Create_Should_IgnoreBlankReasonAndKeys_When_Sent()
    {
        var entry = AuditEntry.Create(
            Now, "owner.viewed", "owner", null, null, null, "  ",
            new Dictionary<string, string?> { [" "] = "x", ["ok"] = "y" }, null, null);

        entry.Reason.Should().BeNull();
        entry.Details.Keys.Should().Equal("ok");
    }

    [Fact]
    public void Create_Should_LimitTheNumberOfDetails_When_TooManyAreSent()
    {
        var details = Enumerable.Range(0, 50).ToDictionary(i => $"k{i}", i => (string?)"v");

        var entry = AuditEntry.Create(Now, "a.b", "account", null, null, null, null, details, null, null);

        entry.Details.Should().HaveCount(AuditEntry.MaxDetails);
    }

    [Fact]
    public void Create_Should_Throw_When_ActionIsMissing()
    {
        var act = () => AuditEntry.Create(Now, " ", "account", null, null, null, null, null, null, null);

        act.Should().Throw<ArgumentException>();
    }
}
