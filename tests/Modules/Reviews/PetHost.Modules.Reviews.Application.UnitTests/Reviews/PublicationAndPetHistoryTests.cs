using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;
using PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews;

/// <summary>O aviso das avaliações publicadas no fim do prazo e o histórico de notas do pet.</summary>
public sealed class PublicationAndPetHistoryTests
{
    private static readonly DateTimeOffset AfterDeadline = ReviewBuilder.CheckedOutAt.AddDays(8);

    private readonly Mock<IReviewRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IReviewNotifier> _notifier = new(MockBehavior.Strict);
    private readonly Mock<IReviewQueries> _queries = new(MockBehavior.Strict);

    public PublicationAndPetHistoryTests()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _notifier
            .Setup(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private NotifyPublishedReviewsCommandHandler NotifySut() =>
        new(_repository.Object, _unitOfWork.Object, _parties.Object, _notifier.Object, new FakeTimeProvider(AfterDeadline));

    // ----- NotifyPublishedReviews -----

    [Fact]
    public async Task Notify_Should_MarkSaveAndEmailEachSubject_When_ReviewsWerePublishedAtTheDeadline()
    {
        // Arrange
        var ownerReview = new ReviewBuilder().Build();
        var hostReview = new ReviewBuilder().ByHost().WithRating(3).Build();
        _repository.Setup(r => r.ListPublishedToNotifyAsync(AfterDeadline, 50, It.IsAny<CancellationToken>())).ReturnsAsync([ownerReview, hostReview]);

        // Act
        var result = await NotifySut().HandleAsync(new NotifyPublishedReviewsCommand(50), CancellationToken.None);

        // Assert
        result.Value!.Notified.Should().Be(2);
        ownerReview.SubjectNotifiedAt.Should().Be(AfterDeadline);
        hostReview.SubjectNotifiedAt.Should().Be(AfterDeadline);
        _notifier.Verify(n => n.NotifyReceivedAsync(ReviewsTestData.HostContact, ReviewParty.Owner, "Camila", 5, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyReceivedAsync(ReviewsTestData.OwnerContact, ReviewParty.Host, "Cida", 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notify_Should_DoNothing_When_ThereIsNothingToNotify()
    {
        // Arrange
        _repository.Setup(r => r.ListPublishedToNotifyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await NotifySut().HandleAsync(new NotifyPublishedReviewsCommand(50), CancellationToken.None);

        // Assert
        result.Value!.Notified.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Notify_Should_SendNothing_When_SavingFails()
    {
        // Arrange
        _repository.Setup(r => r.ListPublishedToNotifyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([new ReviewBuilder().Build()]);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await NotifySut().HandleAsync(new NotifyPublishedReviewsCommand(50), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
        _notifier.Verify(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Notify_Should_SkipTheEmailButStillMark_When_TheSubjectCannotReceiveNotices()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        _repository.Setup(r => r.ListPublishedToNotifyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([review]);
        _parties
            .Setup(p => p.GetContactsAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewContact>());

        // Act
        var result = await NotifySut().HandleAsync(new NotifyPublishedReviewsCommand(50), CancellationToken.None);

        // Assert
        result.Value!.Notified.Should().Be(1);
        review.SubjectNotifiedAt.Should().NotBeNull();
        _notifier.Verify(n => n.NotifyReceivedAsync(It.IsAny<ReviewContact>(), It.IsAny<ReviewParty>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void NotifyValidator_Should_RejectTheBatchSize_When_ItIsOutOfRange(int batchSize)
    {
        // Act & Assert
        new NotifyPublishedReviewsCommandValidator().Validate(new NotifyPublishedReviewsCommand(batchSize)).Errors
            .Should().ContainSingle(e => e.ErrorMessage == ReviewsErrors.BatchSizeInvalid.Message);
    }

    [Fact]
    public async Task Notify_Should_Throw_When_TheCommandIsNull()
    {
        // Act & Assert
        await ((Func<Task>)(() => NotifySut().HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    // ----- ListPetReviews -----

    [Fact]
    public async Task PetHistory_Should_ReturnThePetRatingWithTheHost_When_ThePetWasRated()
    {
        // Arrange
        var review = new ReviewBuilder().ByHost().Build();
        var filter = new ReviewFilter(ReviewParty.Host, PetId: ReviewBuilder.PetId);
        _queries
            .Setup(q => q.ListAsync(filter, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([ReviewsTestData.Row(review)], 1L));
        var sut = new ListPetReviewsQueryHandler(_queries.Object, _parties.Object);

        // Act
        var result = await sut.HandleAsync(new ListPetReviewsQuery(ReviewBuilder.PetId), CancellationToken.None);

        // Assert
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Should().Be(new PetReviewResponse(
            review.Id.Value,
            ReviewBuilder.HostId,
            ReviewBuilder.ListingId,
            new ReviewAuthorSummary("Cida", null),
            4,
            "Latiu um pouco à noite.",
            ReviewBuilder.CheckedOutAt));
    }

    [Fact]
    public async Task PetHistory_Should_LeaveTheHostEmpty_When_TheAccountIsGone()
    {
        // Arrange
        _queries
            .Setup(q => q.ListAsync(It.IsAny<ReviewFilter>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([ReviewsTestData.Row(new ReviewBuilder().ByHost().Build())], 1L));
        _parties.Setup(p => p.GetInfoAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, ReviewPartyInfo>());
        var sut = new ListPetReviewsQueryHandler(_queries.Object, _parties.Object);

        // Act
        var result = await sut.HandleAsync(new ListPetReviewsQuery(ReviewBuilder.PetId), CancellationToken.None);

        // Assert
        result.Value!.Items.Single().Host.Should().BeNull();
    }

    [Fact]
    public async Task PetHistory_Should_ReturnAnEmptyPage_When_ThePetWasNeverRated()
    {
        // Arrange
        _queries.Setup(q => q.ListAsync(It.IsAny<ReviewFilter>(), 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0L));
        var sut = new ListPetReviewsQueryHandler(_queries.Object, _parties.Object);

        // Act
        var result = await sut.HandleAsync(new ListPetReviewsQuery(ReviewBuilder.PetId), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().BeEmpty();
        _parties.Verify(p => p.GetInfoAsync(It.IsAny<ReviewParty>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PetHistory_Should_ReturnValidationErrors_When_ThePetOrPagingIsInvalid()
    {
        // Arrange
        var sut = new ListPetReviewsQueryHandler(_queries.Object, _parties.Object);

        // Act
        var result = await sut.HandleAsync(new ListPetReviewsQuery(Guid.Empty, 0), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.PageInvalid, ReviewsErrors.PetIdInvalid]);
    }

    [Fact]
    public async Task PetHistory_Should_Throw_When_TheQueryIsNull()
    {
        // Act & Assert
        await ((Func<Task>)(() => new ListPetReviewsQueryHandler(_queries.Object, _parties.Object).HandleAsync(null!, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PetSummary_Should_AskThePetScope_When_ThePetIdIsSent()
    {
        // Arrange
        _queries
            .Setup(q => q.GetRatingsAsync(RatingScope.Pet, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == ReviewBuilder.PetId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, RatingRow> { [ReviewBuilder.PetId] = new(4.25m, 4) });

        // Act
        var result = await new GetRatingSummaryQueryHandler(_queries.Object).HandleAsync(new GetRatingSummaryQuery(PetId: ReviewBuilder.PetId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RatingSummaryResponse(4.3m, 4));
    }
}
