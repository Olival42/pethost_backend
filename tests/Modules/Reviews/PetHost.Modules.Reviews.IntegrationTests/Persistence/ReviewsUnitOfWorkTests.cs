using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Infrastructure.Persistence;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.IntegrationTests.Persistence;

/// <summary>
/// O Unit of Work contra o Postgres: grava, traduz o índice único da reserva em
/// <c>REVIEW_ALREADY_EXISTS</c> e a concorrência otimista (<c>xmin</c>) em <c>REVIEW_CONFLICT</c>.
/// </summary>
[Collection(ReviewsApiCollection.Name)]
public sealed class ReviewsUnitOfWorkTests(ReviewsApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChangesAsync_Should_PersistTheReview_When_ItIsNew()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = await SaveAsync(review);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IReviewRepository>().GetByIdAsync(review.Id, Ct);
        stored!.Rating.Should().Be(5);
        stored.Comment.Should().Be("A Pipoca voltou feliz!");
        stored.StayEndedAt.Should().Be(ReviewBuilder.CheckedOutAt);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ReturnAlreadyExists_When_TheBookingAlreadyHasAReview()
    {
        // Arrange
        (await SaveAsync(new ReviewBuilder().Build())).IsSuccess.Should().BeTrue();

        // Act
        var result = await SaveAsync(new ReviewBuilder().WithRating(1).Build());

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.AlreadyExists);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_KeepBothSides_When_EachSideReviewsTheSameBooking()
    {
        // Act
        var owner = await SaveAsync(new ReviewBuilder().Build());
        var host = await SaveAsync(new ReviewBuilder().ByHost().Build());

        // Assert
        owner.IsSuccess.Should().BeTrue();
        host.IsSuccess.Should().BeTrue();
        await using var scope = fixture.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().Reviews.AsNoTracking()
            .SingleAsync(r => r.AuthorSide == ReviewParty.Host, Ct);
        stored.PetRatings.Should().ContainSingle(p => p.PetId == ReviewBuilder.PetId && p.Rating == 4);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ReturnConflict_When_TheReviewChangedAtTheSameTime()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        (await SaveAsync(review)).IsSuccess.Should().BeTrue();

        await using var first = fixture.Services.CreateAsyncScope();
        await using var second = fixture.Services.CreateAsyncScope();
        var mine = await first.ServiceProvider.GetRequiredService<IReviewRepository>().GetByIdAsync(review.Id, Ct);
        var theirs = await second.ServiceProvider.GetRequiredService<IReviewRepository>().GetByIdAsync(review.Id, Ct);

        theirs!.Update(2, null, null, null, ReviewBuilder.DefaultNow);
        (await second.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync(Ct)).IsSuccess.Should().BeTrue();

        // Act
        mine!.Update(3, null, null, null, ReviewBuilder.DefaultNow);
        var result = await first.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync(Ct);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
    }

    [Fact]
    public async Task GetByIdAsync_Should_IgnoreTheReview_When_ItWasDeleted()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Delete(ReviewBuilder.DefaultNow);
        (await SaveAsync(review)).IsSuccess.Should().BeTrue();

        await using var scope = fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReviewRepository>();

        // Act
        var stored = await repository.GetByIdAsync(review.Id, Ct);

        // Assert
        stored.Should().BeNull();
        (await repository.ExistsForBookingAsync(ReviewBuilder.BookingId, ReviewParty.Owner, Ct)).Should().BeTrue();
        (await repository.ExistsForBookingAsync(ReviewBuilder.BookingId, ReviewParty.Host, Ct)).Should().BeFalse();
        (await scope.ServiceProvider.GetRequiredService<ReviewsDbContext>().Reviews.AsNoTracking().CountAsync(Ct)).Should().Be(1);
    }

    private async Task<PetHost.Shared.Kernel.Results.Result> SaveAsync(Review review)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IReviewRepository>().Add(review);

        return await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync(Ct);
    }
}
