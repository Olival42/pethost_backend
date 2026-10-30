using FluentAssertions;
using Moq;
using PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Audit.Application.UnitTests.Entries;

public sealed class SearchAuditEntriesQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAuditEntryRepository> _repository = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly SearchAuditEntriesQueryHandler _sut;

    public SearchAuditEntriesQueryHandlerTests()
    {
        _sut = new SearchAuditEntriesQueryHandler(_repository.Object, _auditTrail.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnThePageWithTotals_When_FiltersAreValid()
    {
        var target = Guid.CreateVersion7();
        var entry = AuditEntry.Create(Now, "owner.suspended", "owner", target, null, "admin", "fraude", null, null, null);
        _repository
            .Setup(r => r.SearchAsync(It.IsAny<AuditEntryFilter>(), 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([entry], 15L));

        var result = await _sut.HandleAsync(new SearchAuditEntriesQuery(2, 10, TargetType: "owner", TargetId: target), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle().Which.Action.Should().Be("owner.suspended");
        result.Value.TotalCount.Should().Be(15);
        result.Value.TotalPages.Should().Be(2);
        _repository.Verify(r => r.SearchAsync(
            It.Is<AuditEntryFilter>(f => f.TargetType == "owner" && f.TargetId == target),
            2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordTheSearchItself_When_Called()
    {
        // Quem lê a trilha vê IP e ações de outras pessoas: a leitura também fica registrada.
        _repository
            .Setup(r => r.SearchAsync(It.IsAny<AuditEntryFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0L));

        await _sut.HandleAsync(new SearchAuditEntriesQuery(Action: "owner.viewed"), CancellationToken.None);

        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.AuditSearched && r.Details!["action"] == "owner.viewed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryError_When_PagingAndPeriodAreInvalid()
    {
        var result = await _sut.HandleAsync(
            new SearchAuditEntriesQuery(0, 500, From: Now, To: Now.AddDays(-1)),
            CancellationToken.None);

        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["page", "pageSize", "from"]);
        _repository.Verify(
            r => r.SearchAsync(It.IsAny<AuditEntryFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
