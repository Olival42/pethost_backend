using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reviews;

/// <summary>Avaliação às cegas: guardada até o fim do prazo, publicada antes se o outro lado avaliar.</summary>
public sealed class ReviewPublicationTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;
    private static readonly DateTimeOffset Deadline = ReviewBuilder.CheckedOutAt.AddDays(Review.WindowDays);

    [Fact]
    public void Create_Should_HoldTheReviewUntilTheEndOfTheWindow_When_Created()
    {
        // Act
        var review = new ReviewBuilder().Build();

        // Assert
        review.PublishedAt.Should().Be(Deadline);
        review.IsPublished(Now).Should().BeFalse();
        review.IsPublished(Deadline).Should().BeTrue();
        review.SubjectNotifiedAt.Should().BeNull();
    }

    [Fact]
    public void Publish_Should_PublishNow_When_TheOtherSideReviewedBeforeTheDeadline()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        var later = Now.AddHours(3);

        // Act
        var changed = review.Publish(later);

        // Assert
        changed.Should().BeTrue();
        review.PublishedAt.Should().Be(later);
        review.UpdatedAt.Should().Be(later);
        review.IsPublished(later).Should().BeTrue();
    }

    [Fact]
    public void Publish_Should_ChangeNothing_When_AlreadyPublished()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Publish(Now);

        // Act
        var changed = review.Publish(Now.AddDays(1));

        // Assert
        changed.Should().BeFalse();
        review.PublishedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_Should_ReturnAlreadyPublished_When_TheReviewWasPublished()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Publish(Now);

        // Act
        var result = review.Update(1, null, null, null, Now.AddHours(1));

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.AlreadyPublished);
        review.Rating.Should().Be(5);
    }

    [Fact]
    public void MarkSubjectNotified_Should_KeepTheFirstTime_When_CalledTwice()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        review.MarkSubjectNotified(Now);
        review.MarkSubjectNotified(Now.AddDays(1));

        // Assert
        review.SubjectNotifiedAt.Should().Be(Now);
    }
}
