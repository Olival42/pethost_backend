using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.HostApplications.PurgeHostApplications;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>A tarefa de limpeza: rascunhos abandonados e prazo de guarda das fotos.</summary>
public sealed class PurgeHostApplicationsTests
{
    private static readonly DateTimeOffset Today = Now.AddDays(10);

    private readonly Mock<IHostApplicationRepository> _applications = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IPrivateImageStorage> _storage = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(Today);

    public PurgeHostApplicationsTests()
    {
        _applications.Setup(r => r.ListDraftsCreatedBeforeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _applications.Setup(r => r.ListWithDocumentsClosedBeforeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private PurgeHostApplicationsCommandHandler Purge(TimeSpan retention) =>
        new(_applications.Object, _unitOfWork.Object, _storage.Object, Policy(retention), _auditTrail.Object, _time);

    [Fact]
    public async Task Purge_Should_ExpireAbandonedDrafts_When_TheyAreOlderThanTheLimit()
    {
        // Arrange
        var draft = WithAllPhotos();
        _applications.Setup(r => r.ListDraftsCreatedBeforeAsync(Today.AddDays(-7), It.IsAny<CancellationToken>())).ReturnsAsync([draft]);
        _applications.Setup(r => r.ListWithDocumentsClosedBeforeAsync(Today, It.IsAny<CancellationToken>())).ReturnsAsync([draft]);

        // Act
        var result = await Purge(TimeSpan.Zero).HandleAsync(new PurgeHostApplicationsCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new Application.HostApplications.Responses.PurgeHostApplicationsResponse(1, 1));
        draft.Status.Should().Be(HostApplicationStatus.Expired);
        draft.Documents.Should().BeEmpty();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationExpired), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationDocumentsPurged && r.Details!["count"] == "3"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Purge_Should_DeletePhotosOfClosedApplications_When_RetentionHasPassed()
    {
        // Arrange
        var decided = Rejected();
        _applications.Setup(r => r.ListWithDocumentsClosedBeforeAsync(Today.AddDays(-5), It.IsAny<CancellationToken>())).ReturnsAsync([decided]);

        // Act
        var result = await Purge(TimeSpan.FromDays(5)).HandleAsync(new PurgeHostApplicationsCommand(), CancellationToken.None);

        // Assert
        result.Value!.PurgedApplications.Should().Be(1);
        decided.DocumentsPurgedAt.Should().Be(Today);
    }

    [Fact]
    public async Task Purge_Should_KeepThePhotosInTheBucket_When_SavingFails()
    {
        // Arrange
        var decided = Rejected();
        _applications.Setup(r => r.ListWithDocumentsClosedBeforeAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([decided]);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("HOST_APPLICATION_CONFLICT", "race")));

        // Act
        var result = await Purge(TimeSpan.Zero).HandleAsync(new PurgeHostApplicationsCommand(), CancellationToken.None);

        // Assert
        result.Value!.PurgedApplications.Should().Be(0);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Purge_Should_DoNothing_When_ThereIsNothingDue()
    {
        // Act
        var result = await Purge(TimeSpan.Zero).HandleAsync(new PurgeHostApplicationsCommand(), CancellationToken.None);

        // Assert
        result.Value!.ExpiredDrafts.Should().Be(0);
        result.Value.PurgedApplications.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
