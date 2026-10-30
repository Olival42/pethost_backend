using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Application.Reviews.UpdateReview;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews.UpdateReview;

public sealed class UpdateReviewCommandHandlerTests
{
    private readonly Mock<IReviewRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IReviewBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(ReviewBuilder.DefaultNow.AddHours(1));
    private readonly Review _review = new ReviewBuilder().WithRating(5).Build();
    private readonly Review _hostReview = new ReviewBuilder().ByHost().Build();
    private readonly UpdateReviewCommandHandler _sut;

    public UpdateReviewCommandHandlerTests()
    {
        _sut = new UpdateReviewCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            _parties.Object,
            _bookings.Object,
            new ReviewResponseBuilder(_parties.Object),
            _auditTrail.Object,
            _time);

        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_review);
        _repository.Setup(r => r.GetByIdAsync(_hostReview.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_hostReview);
        _bookings.Setup(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewBuilder.CompletedStay());
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private UpdateReviewCommand Command(int? rating = null, string? comment = null) =>
        new(ReviewBuilder.OwnerUserId, Roles.Owner, _review.Id.Value, rating, comment);

    [Fact]
    public async Task HandleAsync_Should_SaveAndRecordTheChangedFields_When_TheAuthorChangesTheRating()
    {
        // Act
        var result = await _sut.HandleAsync(Command(rating: 3), CancellationToken.None);

        // Assert
        result.Value!.Rating.Should().Be(3);
        result.Value.BookingId.Should().Be(ReviewBuilder.BookingId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _bookings.Verify(b => b.GetStayAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewUpdated && r.TargetId == _review.Id.Value && r.Details!["fields"] == "rating"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReplaceThePetsCheckingTheStay_When_TheHostSendsPets()
    {
        // Arrange
        var command = new UpdateReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _hostReview.Id.Value, null, null, [new ReviewPetInput(ReviewBuilder.PetId, 2, null)]);

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Value!.Pets.Should().ContainSingle(p => p.Rating == 2);
        _bookings.Verify(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPetNotInStay_When_TheBookingIsGone()
    {
        // Arrange
        _bookings.Setup(b => b.GetStayAsync(ReviewBuilder.BookingId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewedStay?)null);
        var command = new UpdateReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _hostReview.Id.Value, null, null, [new ReviewPetInput(ReviewBuilder.PetId, 2, null)]);

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetNotInStay);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_NothingChanges()
    {
        // Act
        var result = await _sut.HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.Value!.Rating.Should().Be(5);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_review.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await _sut.HandleAsync(Command(rating: 1), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReviewWasWrittenByTheOtherSide()
    {
        // Arrange: a anfitriã tenta editar a avaliação da tutora.
        var command = new UpdateReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _review.Id.Value, 1, null);

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_review.Id));
        _review.Rating.Should().Be(5);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerDeactivated_When_TheOwnerIsNotAvailable()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ReviewBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableOwner);

        // Act
        var result = await _sut.HandleAsync(Command(rating: 1), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.OwnerDeactivated);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnWindowClosed_When_TheEditWindowIsOver()
    {
        // Arrange
        _time.SetUtcNow(ReviewBuilder.CheckedOutAt.AddDays(8));

        // Act
        var result = await _sut.HandleAsync(Command(rating: 1), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.WindowClosed);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PropagateTheSaveError_When_TheReviewChangedAtTheSameTime()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(Command(rating: 2), CancellationToken.None);

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
