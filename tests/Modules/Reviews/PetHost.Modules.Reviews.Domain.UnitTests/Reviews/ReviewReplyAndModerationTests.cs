using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reviews;

/// <summary>A resposta do avaliado e a ocultação pelo admin.</summary>
public sealed class ReviewReplyAndModerationTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;
    private static readonly Guid AdminId = Guid.Parse("0199d000-0000-7000-8000-0000000000aa");

    /// <summary>Às cegas, só a avaliação publicada pode ser respondida: publica na hora.</summary>
    private static Review Published(ReviewBuilder builder)
    {
        var review = builder.Build();
        review.Publish(Now);
        return review;
    }

    // ----- Reply -----

    [Fact]
    public void Reply_Should_ReturnNotFound_When_TheReviewIsNotPublishedYet()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Oi", Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(review.Id));
    }

    [Fact]
    public void Reply_Should_StoreTheTrimmedReply_When_TheHostAnswersTheOwnersReview()
    {
        // Arrange
        var review = Published(new ReviewBuilder());
        var later = Now.AddDays(30);

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "  Obrigada, Camila!  ", later);

        // Assert
        result.Value.Should().BeTrue();
        review.ReplyText.Should().Be("Obrigada, Camila!");
        review.RepliedAt.Should().Be(later);
        review.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Reply_Should_LetTheOwnerAnswer_When_TheHostWroteTheReview()
    {
        // Arrange
        var review = Published(new ReviewBuilder().ByHost());

        // Act
        var result = review.Reply(ReviewParty.Owner, ReviewBuilder.OwnerId, "Obrigada!", Now);

        // Assert
        result.Value.Should().BeTrue();
    }

    [Fact]
    public void Reply_Should_ChangeNothing_When_TheReplyIsTheSame()
    {
        // Arrange
        var review = Published(new ReviewBuilder());
        review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", Now);

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", Now.AddDays(1));

        // Assert
        result.Value.Should().BeFalse();
        review.RepliedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(ReviewParty.Owner)]
    [InlineData(ReviewParty.Host)]
    public void Reply_Should_ReturnNotFound_When_TheReplierIsNotTheReviewedParty(ReviewParty party)
    {
        // Arrange
        var review = Published(new ReviewBuilder());
        var profile = party == ReviewParty.Owner ? ReviewBuilder.OwnerId : Guid.CreateVersion7();

        // Act
        var result = review.Reply(party, profile, "Oi", Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(review.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Reply_Should_ReturnReplyRequired_When_TheTextIsEmpty(string? text)
    {
        // Arrange
        var review = Published(new ReviewBuilder());

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, text, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReplyRequired);
    }

    [Fact]
    public void Reply_Should_ReturnReplyTooLong_When_TheTextIsTooLong()
    {
        // Arrange
        var review = Published(new ReviewBuilder());

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, new string('a', Review.CommentMaxLength + 1), Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReplyTooLong);
    }

    [Fact]
    public void Reply_Should_ReturnReplyInvalidCharacters_When_TheTextHasAControlCharacter()
    {
        // Arrange
        var review = Published(new ReviewBuilder());

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "a\u0007b", Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReplyInvalidCharacters);
    }

    [Fact]
    public void Reply_Should_ReturnHiddenByModeration_When_TheAdminHidTheReview()
    {
        // Arrange
        var review = Published(new ReviewBuilder());
        review.Hide(AdminId, "Ofensivo", Now);

        // Act
        var result = review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Oi", Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HiddenByModeration);
    }

    [Fact]
    public void RemoveReply_Should_ClearTheReply_When_TheReviewedPartyRemovesIt()
    {
        // Arrange
        var review = Published(new ReviewBuilder());
        review.Reply(ReviewParty.Host, ReviewBuilder.HostId, "Obrigada!", Now);

        // Act
        var result = review.RemoveReply(ReviewParty.Host, ReviewBuilder.HostId, Now.AddDays(1));

        // Assert
        result.Value.Should().BeTrue();
        review.ReplyText.Should().BeNull();
        review.RepliedAt.Should().BeNull();
    }

    [Fact]
    public void RemoveReply_Should_ChangeNothing_When_ThereIsNoReply()
    {
        // Arrange
        var review = Published(new ReviewBuilder());

        // Act
        var result = review.RemoveReply(ReviewParty.Host, ReviewBuilder.HostId, Now);

        // Assert
        result.Value.Should().BeFalse();
    }

    [Fact]
    public void RemoveReply_Should_ReturnNotFound_When_TheCallerIsNotTheReviewedParty()
    {
        // Arrange
        var review = Published(new ReviewBuilder());

        // Act
        var result = review.RemoveReply(ReviewParty.Owner, ReviewBuilder.OwnerId, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(review.Id));
    }

    // ----- Hide / Unhide -----

    [Fact]
    public void Hide_Should_RecordWhoWhenAndWhy_When_TheReviewIsVisible()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var changed = review.Hide(AdminId, "Expõe o telefone da anfitriã.", Now);

        // Assert
        changed.Should().BeTrue();
        review.IsHidden.Should().BeTrue();
        review.HiddenAt.Should().Be(Now);
        review.HiddenBy.Should().Be(AdminId);
        review.HiddenReason.Should().Be("Expõe o telefone da anfitriã.");
    }

    [Fact]
    public void Hide_Should_KeepTheFirstHiding_When_CalledTwice()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Hide(AdminId, "Primeiro", Now);

        // Act
        var changed = review.Hide(AdminId, "Segundo", Now.AddDays(1));

        // Assert
        changed.Should().BeFalse();
        review.HiddenReason.Should().Be("Primeiro");
    }

    [Fact]
    public void Hide_Should_Throw_When_TheAdminIsEmpty()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var act = () => review.Hide(Guid.Empty, "Motivo", Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hide_Should_Throw_When_TheReasonIsEmpty()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act
        var act = () => review.Hide(AdminId, " ", Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unhide_Should_ClearTheHiding_When_TheReviewIsHidden()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Hide(AdminId, "Motivo", Now);

        // Act
        var changed = review.Unhide(Now.AddDays(1));

        // Assert
        changed.Should().BeTrue();
        review.IsHidden.Should().BeFalse();
        review.HiddenBy.Should().BeNull();
        review.HiddenReason.Should().BeNull();
    }

    [Fact]
    public void Unhide_Should_ChangeNothing_When_TheReviewIsVisible()
    {
        // Arrange
        var review = new ReviewBuilder().Build();

        // Act & Assert
        review.Unhide(Now).Should().BeFalse();
    }

    [Fact]
    public void Update_Should_ReturnHiddenByModeration_When_TheAdminHidTheReview()
    {
        // Arrange
        var review = new ReviewBuilder().Build();
        review.Hide(AdminId, "Motivo", Now);

        // Act
        var result = review.Update(1, null, null, null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HiddenByModeration);
    }
}
