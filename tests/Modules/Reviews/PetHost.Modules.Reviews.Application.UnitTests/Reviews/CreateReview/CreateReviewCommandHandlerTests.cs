using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Application.Reviews.CreateReview;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews.CreateReview;

public sealed class CreateReviewCommandHandlerTests
{
    private static readonly CreateReviewCommand OwnerCommand = new(ReviewBuilder.OwnerUserId, Roles.Owner, ReviewBuilder.BookingId, 5, "Recomendo!");

    private static readonly CreateReviewCommand HostCommand = new(
        ReviewBuilder.HostUserId,
        Roles.Host,
        ReviewBuilder.BookingId,
        4,
        "Tutora atenciosa.",
        [new ReviewPetInput(ReviewBuilder.PetId, 5, "Um amor.")]);

    private readonly Mock<IReviewRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IReviewBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IReviewNotifier> _notifier = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly CreateReviewCommandHandler _sut;

    private Review? _added;

    public CreateReviewCommandHandlerTests()
    {
        _sut = new CreateReviewCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            _parties.Object,
            _bookings.Object,
            _notifier.Object,
            new ReviewResponseBuilder(_parties.Object),
            _auditTrail.Object,
            new FakeTimeProvider(ReviewBuilder.DefaultNow));

        _bookings.Setup(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewBuilder.CompletedStay());
        _repository.Setup(r => r.ExistsForBookingAsync(ReviewBuilder.BookingId, It.IsAny<ReviewParty>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.Add(It.IsAny<Review>())).Callback<Review>(r => _added = r);
        _repository.Setup(r => r.FindByBookingAsync(ReviewBuilder.BookingId, It.IsAny<ReviewParty>(), It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _notifier
            .Setup(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveAndRecordButHoldTheReview_When_TheHostHasNotReviewedYet()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(_added!.Id.Value);
        result.Value.AuthorSide.Should().Be("owner");
        result.Value.HostId.Should().Be(ReviewBuilder.HostId);
        result.Value.ListingId.Should().Be(ReviewBuilder.ListingId);
        result.Value.OwnerId.Should().BeNull();
        result.Value.Pets.Should().BeNull();
        result.Value.Author.Should().Be(new ReviewPartySummary("Camila", "https://img.pethost.test/camila.png"));
        result.Value.BookingId.Should().Be(ReviewBuilder.BookingId);
        result.Value.EditableUntil.Should().Be(ReviewBuilder.CheckedOutAt.AddDays(7));
        _repository.Verify(r => r.ExistsForBookingAsync(ReviewBuilder.BookingId, ReviewParty.Owner, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewCreated && r.TargetType == AuditTargets.Review && r.Details!["authorSide"] == "owner" && r.Details["rating"] == "5"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        result.Value.PublishedAt.Should().Be(ReviewBuilder.CheckedOutAt.AddDays(7));
        _added.SubjectNotifiedAt.Should().BeNull();
        _repository.Verify(r => r.FindByBookingAsync(ReviewBuilder.BookingId, ReviewParty.Host, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PublishBothAndNotifyBothSides_When_TheHostAlreadyReviewed()
    {
        // Arrange
        var hostReview = new ReviewBuilder().ByHost().Build();
        _repository.Setup(r => r.FindByBookingAsync(ReviewBuilder.BookingId, ReviewParty.Host, It.IsAny<CancellationToken>())).ReturnsAsync(hostReview);

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.Value!.PublishedAt.Should().Be(ReviewBuilder.DefaultNow);
        hostReview.PublishedAt.Should().Be(ReviewBuilder.DefaultNow);
        _added!.SubjectNotifiedAt.Should().Be(ReviewBuilder.DefaultNow);
        hostReview.SubjectNotifiedAt.Should().Be(ReviewBuilder.DefaultNow);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyReceivedAsync(ReviewsTestData.HostContact, ReviewParty.Owner, "Camila", 5, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyReceivedAsync(ReviewsTestData.OwnerContact, ReviewParty.Host, "Cida", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveTheOwnerAndPetRatings_When_TheHostReviews()
    {
        // Act
        var result = await _sut.HandleAsync(HostCommand, CancellationToken.None);

        // Assert
        result.Value!.AuthorSide.Should().Be("host");
        result.Value.OwnerId.Should().Be(ReviewBuilder.OwnerId);
        result.Value.Author!.Name.Should().Be("Cida");
        result.Value.Pets.Should().ContainSingle().Which.Should().Be(new ReviewPetResponse(ReviewBuilder.PetId, "Pipoca", 5, "Um amor."));
        _added!.AuthorSide.Should().Be(ReviewParty.Host);
        _repository.Verify(r => r.FindByBookingAsync(ReviewBuilder.BookingId, ReviewParty.Owner, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotifyOnlyWhoCanReceiveNotices_When_BothArePublished()
    {
        // Arrange
        _repository
            .Setup(r => r.FindByBookingAsync(ReviewBuilder.BookingId, ReviewParty.Host, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviewBuilder().ByHost().Build());
        _parties
            .Setup(p => p.GetContactsAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewContact> { [ReviewBuilder.HostId] = ReviewsTestData.HostContact with { IsReachable = false } });

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(n => n.NotifyReceivedAsync(ReviewsTestData.OwnerContact, ReviewParty.Host, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), ReviewParty.Owner, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_TheAccountHasNoOwnerProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewerProfile?)null);

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.OwnerNotFound);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerDeactivated_When_TheOwnerIsNotAvailable()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableOwner);

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.OwnerDeactivated);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_TheAccountHasNoHostProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewerProfile?)null);

        // Act
        var result = await _sut.HandleAsync(HostCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostNotFound);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivated_When_TheHostIsNotAvailable()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableHost);

        // Act
        var result = await _sut.HandleAsync(HostCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostDeactivated);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnForbidden_When_TheRoleIsNeitherOwnerNorHost()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerCommand with { Role = Roles.Admin }, CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be(ErrorCodes.Forbidden);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBookingNotFound_When_TheBookingDoesNotExist()
    {
        // Arrange
        _bookings.Setup(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewedStay?)null);

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotFound(ReviewBuilder.BookingId));
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheDomainError_When_TheStayIsNotCompleted()
    {
        // Arrange
        _bookings
            .Setup(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReviewBuilder.CompletedStay() with { IsCompleted = false, CheckedOutAt = null });

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotCompleted);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAlreadyExists_When_ThisSideAlreadyReviewed()
    {
        // Arrange
        _repository.Setup(r => r.ExistsForBookingAsync(ReviewBuilder.BookingId, ReviewParty.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.AlreadyExists);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task HandleAsync_Should_PropagateTheSaveError_When_TheDatabaseRejectsTheReview()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.AlreadyExists));

        // Act
        var result = await _sut.HandleAsync(OwnerCommand, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.AlreadyExists);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_When_TheCommandIsNull()
    {
        // Act
        var act = () => _sut.HandleAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private void VerifyNothingSaved()
    {
        _repository.Verify(r => r.Add(It.IsAny<Review>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
