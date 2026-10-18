using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reminders;

public sealed class SendReviewRemindersCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.CheckedOutAt.AddDays(2);
    private static readonly SendReviewRemindersCommand Command = new(DelayHours: 24, BatchSize: 50);

    private readonly Mock<IReviewBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IReviewRepository> _reviews = new(MockBehavior.Strict);
    private readonly Mock<IReviewReminderRepository> _reminders = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IReviewNotifier> _notifier = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly List<ReviewReminder> _added = [];
    private readonly SendReviewRemindersCommandHandler _sut;

    public SendReviewRemindersCommandHandlerTests()
    {
        _sut = new SendReviewRemindersCommandHandler(
            _bookings.Object,
            _reviews.Object,
            _reminders.Object,
            _unitOfWork.Object,
            _parties.Object,
            _notifier.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _bookings
            .Setup(b => b.ListCompletedAsync(Now.AddDays(-7), Now.AddHours(-24), 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ReviewBuilder.CompletedStay()]);
        GivenReviewed();
        GivenSent();
        _reminders.Setup(r => r.Add(It.IsAny<ReviewReminder>())).Callback<ReviewReminder>(_added.Add);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _notifier
            .Setup(n => n.SendReminderAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private void GivenReviewed(params ReviewParty[] sides) =>
        _reviews
            .Setup(r => r.GetReviewedSidesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sides.Select(s => (ReviewBuilder.BookingId, s)).ToHashSet());

    private void GivenSent(params ReviewParty[] sides) =>
        _reminders
            .Setup(r => r.GetSentAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sides.Select(s => (ReviewBuilder.BookingId, s)).ToHashSet());

    [Fact]
    public async Task HandleAsync_Should_RemindBothSidesAndRecord_When_NobodyReviewedYet()
    {
        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(2);
        _added.Select(r => r.Side).Should().BeEquivalentTo([ReviewParty.Owner, ReviewParty.Host]);
        var deadline = ReviewBuilder.CheckedOutAt.AddDays(7);
        _notifier.Verify(n => n.SendReminderAsync(ReviewsTestData.OwnerContact, ReviewParty.Owner, deadline, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.SendReminderAsync(ReviewsTestData.HostContact, ReviewParty.Host, deadline, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReminderSent), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_Should_SkipTheSidesThatReviewedOrWereReminded_When_Asked()
    {
        // Arrange
        GivenReviewed(ReviewParty.Owner);
        GivenSent(ReviewParty.Host);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SkipWhoCannotReceiveNotices_When_TheContactIsUnreachable()
    {
        // Arrange
        _parties
            .Setup(p => p.GetContactsAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewContact> { [ReviewBuilder.HostId] = ReviewsTestData.HostContact with { IsReachable = false } });

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(1);
        _added.Should().ContainSingle(r => r.Side == ReviewParty.Owner);
    }

    [Fact]
    public async Task HandleAsync_Should_SendNothing_When_NobodyCanReceiveNotices()
    {
        // Arrange
        _parties.Setup(p => p.GetContactsAsync(It.IsAny<ReviewParty>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, ReviewContact>());

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DoNothing_When_ThereAreNoCompletedStays()
    {
        // Arrange
        _bookings
            .Setup(b => b.ListCompletedAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.EmailsSent.Should().Be(0);
        _reviews.Verify(r => r.GetReviewedSidesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SendNothing_When_SavingTheRemindersFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
        _notifier.Verify(n => n.SendReminderAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_When_TheCommandIsNull()
    {
        // Act
        var act = () => _sut.HandleAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0, 10, "DelayHours")]
    [InlineData(168, 10, "DelayHours")]
    [InlineData(24, 0, "BatchSize")]
    [InlineData(24, 1001, "BatchSize")]
    public void Validator_Should_RejectTheSetting_When_ItIsOutOfRange(int delayHours, int batchSize, string property)
    {
        // Act
        var result = new SendReviewRemindersCommandValidator().Validate(new SendReviewRemindersCommand(delayHours, batchSize));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == property);
    }

    [Fact]
    public void Validator_Should_Pass_When_TheSettingsAreValid()
    {
        // Act & Assert
        new SendReviewRemindersCommandValidator().Validate(Command).IsValid.Should().BeTrue();
    }
}
