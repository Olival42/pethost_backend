using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.DeleteReview;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews.DeleteReview;

public sealed class DeleteReviewCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.CheckedOutAt.AddDays(60);

    private readonly Mock<IReviewRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IReviewReportRepository> _reports = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Review _review = new ReviewBuilder().Build();
    private readonly DeleteReviewCommandHandler _sut;

    public DeleteReviewCommandHandlerTests()
    {
        _sut = new DeleteReviewCommandHandler(_repository.Object, _reports.Object, _unitOfWork.Object, _parties.Object, _auditTrail.Object, new FakeTimeProvider(Now));

        _reports.Setup(r => r.ListPendingForReviewAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_review);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private DeleteReviewCommand Command => new(ReviewBuilder.OwnerUserId, Roles.Owner, _review.Id.Value);

    [Fact]
    public async Task HandleAsync_Should_SoftDeleteAndRecord_When_TheAuthorDeletesAfterTheEditWindow()
    {
        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value.Should().Be(Unit.Value);
        _review.DeletedAt.Should().Be(Now);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewDeleted && r.TargetId == _review.Id.Value), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_CloseThePendingReportsAndRecordThem_When_TheReviewWasReported()
    {
        // Arrange
        var report = ReviewReport.Submit(_review, ReviewParty.Host, ReviewBuilder.HostId, ReviewBuilder.HostUserId, ReviewReportReason.Offensive, null, Now).Value!;
        _reports.Setup(r => r.ListPendingForReviewAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync([report]);

        // Act
        await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        report.Status.Should().Be(ReviewReportStatus.Closed);
        report.ResolvedAt.Should().Be(Now);
        report.ResolvedBy.Should().BeNull();
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReportResolved && r.TargetId == report.Id.Value && r.Details!["outcome"] == "closed"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_AllowTheDeletion_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableOwner);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _review.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_TheAccountHasNoOwnerProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewerProfile?)null);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.OwnerNotFound);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReviewBelongsToAnotherOwner()
    {
        // Arrange
        _parties
            .Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviewerProfile(Guid.CreateVersion7(), IsAvailable: true));

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
        _review.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_PropagateTheSaveError_When_TheReviewChangedAtTheSameTime()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_When_TheCommandIsNull()
    {
        // Act
        var act = () => _sut.HandleAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
