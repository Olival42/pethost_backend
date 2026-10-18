using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;
using PetHost.Modules.Reviews.Application.Reviews.GetReviewById;
using PetHost.Modules.Reviews.Application.Reviews.ListMyReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListOwnerReviews;
using PetHost.Modules.Reviews.Application.Reviews.ListReviews;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reviews;

/// <summary>As leituras: a lista pública, uma avaliação (quem vê o quê), as minhas, as do tutor e a nota.</summary>
public sealed class ReviewQueriesHandlersTests
{
    private static readonly Guid AdminUserId = Guid.Parse("0199d000-0000-7000-8000-0000000000aa");
    private static readonly Guid StrangerUserId = Guid.Parse("0199d000-0000-7000-8000-0000000000bb");

    /// <summary>Depois do prazo: as avaliações do builder já estão publicadas.</summary>
    private static readonly FakeTimeProvider AfterDeadline = new(ReviewBuilder.CheckedOutAt.AddDays(8));

    private readonly Mock<IReviewQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Review _ownerReview = new ReviewBuilder().Build();
    private readonly Review _hostReview = new ReviewBuilder().ByHost().Build();
    private readonly ReviewResponseBuilder _builder;

    public ReviewQueriesHandlersTests()
    {
        _builder = new ReviewResponseBuilder(_parties.Object);
        _parties.Setup(p => p.FindOwnerByAccountAsync(StrangerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(new ReviewerProfile(Guid.CreateVersion7(), true));
    }

    // ----- ListReviews -----

    [Fact]
    public async Task ListReviews_Should_ReturnTheOwnersReviewsAsPublic_When_FilteredByHostAndListing()
    {
        // Arrange
        var filter = new ReviewFilter(ReviewParty.Owner, ReviewBuilder.HostId, ReviewBuilder.ListingId);
        _queries.Setup(q => q.ListAsync(filter, 2, 10, It.IsAny<CancellationToken>())).ReturnsAsync(([ReviewsTestData.Row(_ownerReview)], 11L));
        var sut = new ListReviewsQueryHandler(_queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListReviewsQuery(ReviewBuilder.HostId, ReviewBuilder.ListingId, 2, 10), CancellationToken.None);

        // Assert
        result.Value!.TotalCount.Should().Be(11);
        var item = result.Value.Items.Should().ContainSingle().Subject;
        item.Author!.Name.Should().Be("Camila");
        item.BookingId.Should().BeNull();
        item.EditableUntil.Should().BeNull();
        item.Moderation.Should().BeNull();
    }

    [Fact]
    public async Task ListReviews_Should_ReturnAllValidationErrors_When_PagingAndIdsAreInvalid()
    {
        // Arrange
        var sut = new ListReviewsQueryHandler(_queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListReviewsQuery(Guid.Empty, Guid.Empty, 0, 101), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo(
        [
            ReviewsErrors.PageInvalid, ReviewsErrors.PageSizeInvalid, ReviewsErrors.HostIdInvalid, ReviewsErrors.ListingIdInvalid,
        ]);
    }

    [Fact]
    public async Task ListReviews_Should_ReturnAnEmptyPageWithoutLookingUpAuthors_When_ThereAreNoReviews()
    {
        // Arrange
        _queries.Setup(q => q.ListAsync(new ReviewFilter(ReviewParty.Owner), 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0L));
        var sut = new ListReviewsQueryHandler(_queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListReviewsQuery(), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().BeEmpty();
        _parties.Verify(p => p.GetInfoAsync(It.IsAny<ReviewParty>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- GetReviewById -----

    private Task<PetHost.Shared.Kernel.Results.Result<ReviewResponse>> GetAsync(Review review, Guid? userId, string? role, bool hidden = false, TimeProvider? time = null)
    {
        var row = ReviewsTestData.Row(review) with { HiddenAt = hidden ? ReviewBuilder.DefaultNow : null, HiddenReason = hidden ? "Ofensivo" : null };
        _queries.Setup(q => q.GetAsync(review.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        return new GetReviewByIdQueryHandler(_queries.Object, _parties.Object, _builder, time ?? AfterDeadline)
            .HandleAsync(new GetReviewByIdQuery(review.Id.Value, userId, role), CancellationToken.None);
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnThePublicView_When_AnonymousReadsTheOwnersReview()
    {
        // Act
        var result = await GetAsync(_ownerReview, null, null);

        // Assert
        result.Value!.Id.Should().Be(_ownerReview.Id.Value);
        result.Value.BookingId.Should().BeNull();
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnNotFound_When_AnonymousReadsTheHostsReview()
    {
        // Act
        var result = await GetAsync(_hostReview, null, null);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_hostReview.Id));
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnNotFound_When_AnotherOwnerReadsTheHostsReview()
    {
        // Act
        var result = await GetAsync(_hostReview, StrangerUserId, Roles.Owner);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_hostReview.Id));
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnTheReviewWithPets_When_TheReviewedOwnerReadsIt()
    {
        // Act
        var result = await GetAsync(_hostReview, ReviewBuilder.OwnerUserId, Roles.Owner);

        // Assert
        result.Value!.OwnerId.Should().Be(ReviewBuilder.OwnerId);
        result.Value.Pets.Should().ContainSingle(p => p.Name == "Pipoca" && p.Rating == 4);
        result.Value.BookingId.Should().BeNull();
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnTheReview_When_AnyHostReadsTheHostsReview()
    {
        // Arrange
        var otherHost = Guid.CreateVersion7();
        _parties.Setup(p => p.FindHostByAccountAsync(otherHost, It.IsAny<CancellationToken>())).ReturnsAsync(new ReviewerProfile(Guid.CreateVersion7(), true));

        // Act
        var result = await GetAsync(_hostReview, otherHost, Roles.Host);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnNotFound_When_TheReviewIsNotPublishedYet()
    {
        // Act
        var result = await GetAsync(_ownerReview, null, null, time: new FakeTimeProvider(ReviewBuilder.DefaultNow));

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_ownerReview.Id));
    }

    [Fact]
    public async Task GetReviewById_Should_ShowThePublicationDate_When_TheAuthorReadsTheHeldReview()
    {
        // Act
        var result = await GetAsync(_ownerReview, ReviewBuilder.OwnerUserId, Roles.Owner, time: new FakeTimeProvider(ReviewBuilder.DefaultNow));

        // Assert
        result.Value!.PublishedAt.Should().Be(ReviewBuilder.CheckedOutAt.AddDays(7));
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnNotFound_When_ThePublicReadsAHiddenReview()
    {
        // Act
        var result = await GetAsync(_ownerReview, null, null, hidden: true);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_ownerReview.Id));
    }

    [Fact]
    public async Task GetReviewById_Should_ShowTheModeration_When_TheAuthorReadsTheHiddenReview()
    {
        // Act
        var result = await GetAsync(_ownerReview, ReviewBuilder.OwnerUserId, Roles.Owner, hidden: true);

        // Assert
        result.Value!.Moderation.Should().Be(new ReviewModerationResponse(ReviewBuilder.DefaultNow, "Ofensivo"));
        result.Value.BookingId.Should().Be(ReviewBuilder.BookingId);
    }

    [Fact]
    public async Task GetReviewById_Should_ShowEverything_When_TheAdminReads()
    {
        // Act
        var result = await GetAsync(_hostReview, AdminUserId, Roles.Admin, hidden: true);

        // Assert
        result.Value!.Moderation.Should().NotBeNull();
        result.Value.BookingId.Should().Be(ReviewBuilder.BookingId);
    }

    [Fact]
    public async Task GetReviewById_Should_ReturnNotFound_When_TheReviewDoesNotExistOrWasDeleted()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        _queries.Setup(q => q.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewRow?)null);
        var sut = new GetReviewByIdQueryHandler(_queries.Object, _parties.Object, _builder, AfterDeadline);

        // Act
        var result = await sut.HandleAsync(new GetReviewByIdQuery(id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(new ReviewId(id)));
    }

    // ----- ListMyReviews -----

    [Fact]
    public async Task ListMyReviews_Should_ReturnWhatTheOwnerWroteIncludingHidden_When_NotReceived()
    {
        // Arrange
        var filter = new ReviewFilter(ReviewParty.Owner, OwnerId: ReviewBuilder.OwnerId, IncludeHidden: true, IncludeUnpublished: true);
        _queries.Setup(q => q.ListAsync(filter, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([ReviewsTestData.Row(_ownerReview)], 1L));
        var sut = new ListMyReviewsQueryHandler(_parties.Object, _queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListMyReviewsQuery(ReviewBuilder.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.BookingId.Should().Be(ReviewBuilder.BookingId);
        item.EditableUntil.Should().Be(ReviewBuilder.CheckedOutAt.AddDays(7));
    }

    [Fact]
    public async Task ListMyReviews_Should_ReturnWhatTheHostReceivedAsPublic_When_Received()
    {
        // Arrange
        var filter = new ReviewFilter(ReviewParty.Owner, HostId: ReviewBuilder.HostId);
        _queries.Setup(q => q.ListAsync(filter, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([ReviewsTestData.Row(_ownerReview)], 1L));
        var sut = new ListMyReviewsQueryHandler(_parties.Object, _queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListMyReviewsQuery(ReviewBuilder.HostUserId, Roles.Host, Received: true), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle().Which.BookingId.Should().BeNull();
    }

    [Fact]
    public async Task ListMyReviews_Should_ReturnHostNotFound_When_TheAccountHasNoHostProfile()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewerProfile?)null);
        var sut = new ListMyReviewsQueryHandler(_parties.Object, _queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListMyReviewsQuery(ReviewBuilder.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostNotFound);
    }

    [Fact]
    public async Task ListMyReviews_Should_ReturnValidationErrors_When_PagingIsInvalid()
    {
        // Arrange
        var sut = new ListMyReviewsQueryHandler(_parties.Object, _queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListMyReviewsQuery(ReviewBuilder.OwnerUserId, Roles.Owner, Page: 0, PageSize: 0), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.PageInvalid, ReviewsErrors.PageSizeInvalid]);
    }

    // ----- ListOwnerReviews -----

    [Fact]
    public async Task ListOwnerReviews_Should_ReturnTheHostsReviewsOfTheOwner_When_Asked()
    {
        // Arrange
        var filter = new ReviewFilter(ReviewParty.Host, OwnerId: ReviewBuilder.OwnerId);
        _queries.Setup(q => q.ListAsync(filter, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([ReviewsTestData.Row(_hostReview)], 1L));
        var sut = new ListOwnerReviewsQueryHandler(_queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListOwnerReviewsQuery(ReviewBuilder.OwnerId), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle().Which.Pets.Should().ContainSingle();
    }

    [Fact]
    public async Task ListOwnerReviews_Should_ReturnValidationErrors_When_TheOwnerIsEmpty()
    {
        // Arrange
        var sut = new ListOwnerReviewsQueryHandler(_queries.Object, _builder);

        // Act
        var result = await sut.HandleAsync(new ListOwnerReviewsQuery(Guid.Empty, 0), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.PageInvalid, ReviewsErrors.OwnerIdInvalid]);
    }

    // ----- GetRatingSummary -----

    [Fact]
    public async Task GetRatingSummary_Should_RoundTheAverageToOneDecimal_When_ThereAreReviews()
    {
        // Arrange
        _queries
            .Setup(q => q.GetRatingsAsync(RatingScope.Listing, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == ReviewBuilder.ListingId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, RatingRow> { [ReviewBuilder.ListingId] = new(4.67m, 3) });
        var sut = new GetRatingSummaryQueryHandler(_queries.Object);

        // Act
        var result = await sut.HandleAsync(new GetRatingSummaryQuery(ListingId: ReviewBuilder.ListingId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RatingSummaryResponse(4.7m, 3));
    }

    [Fact]
    public async Task GetRatingSummary_Should_ReturnZero_When_ThereAreNoReviews()
    {
        // Arrange
        _queries.Setup(q => q.GetRatingsAsync(RatingScope.Owner, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, RatingRow>());
        var sut = new GetRatingSummaryQueryHandler(_queries.Object);

        // Act
        var result = await sut.HandleAsync(new GetRatingSummaryQuery(OwnerId: ReviewBuilder.OwnerId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RatingSummaryResponse(null, 0));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task GetRatingSummary_Should_ReturnRatingScopeInvalid_When_NotExactlyOneIdIsSent(bool host, bool listing)
    {
        // Arrange
        var sut = new GetRatingSummaryQueryHandler(_queries.Object);
        var query = new GetRatingSummaryQuery(host ? ReviewBuilder.HostId : null, listing ? ReviewBuilder.ListingId : null);

        // Act
        var result = await sut.HandleAsync(query, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.RatingScopeInvalid);
    }

    [Fact]
    public async Task GetRatingSummary_Should_ReturnRatingScopeInvalid_When_TheIdIsEmpty()
    {
        // Act
        var result = await new GetRatingSummaryQueryHandler(_queries.Object).HandleAsync(new GetRatingSummaryQuery(HostId: Guid.Empty), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.RatingScopeInvalid);
    }

    // ----- Builder e nulos -----

    [Fact]
    public async Task BuildAsync_Should_LeaveTheAuthorAndPetNameEmpty_When_TheyAreGone()
    {
        // Arrange
        _parties.Setup(p => p.GetInfoAsync(ReviewParty.Host, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, ReviewPartyInfo>());
        _parties.Setup(p => p.GetPetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, string>());

        // Act
        var response = await _builder.BuildAsync(ReviewsTestData.Row(_hostReview), ReviewView.Public, CancellationToken.None);

        // Assert
        response.Author.Should().BeNull();
        response.Pets.Should().ContainSingle().Which.Name.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_Should_IncludeTheReply_When_TheReviewWasAnswered()
    {
        // Arrange
        _ownerReview.Publish(ReviewBuilder.DefaultNow);
        _ownerReview.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", ReviewBuilder.DefaultNow);

        // Act
        var response = await _builder.BuildAsync(ReviewsTestData.Row(_ownerReview), ReviewView.Public, CancellationToken.None);

        // Assert
        response.Reply.Should().Be(new ReviewReplyResponse("Obrigada!", ReviewBuilder.DefaultNow));
    }

    [Fact]
    public async Task Handlers_Should_Throw_When_TheInputIsNull()
    {
        // Arrange
        var list = new ListReviewsQueryHandler(_queries.Object, _builder);
        var get = new GetReviewByIdQueryHandler(_queries.Object, _parties.Object, _builder, AfterDeadline);
        var mine = new ListMyReviewsQueryHandler(_parties.Object, _queries.Object, _builder);
        var owner = new ListOwnerReviewsQueryHandler(_queries.Object, _builder);
        var summary = new GetRatingSummaryQueryHandler(_queries.Object);

        // Act & Assert
        await ((Func<Task>)(() => list.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => get.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => mine.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => owner.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => summary.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _builder.BuildAsync((IReadOnlyList<ReviewRow>)null!, ReviewView.Public, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        ((Action)(() => ReviewResponseBuilder.ToRow(null!))).Should().Throw<ArgumentNullException>();
    }
}
