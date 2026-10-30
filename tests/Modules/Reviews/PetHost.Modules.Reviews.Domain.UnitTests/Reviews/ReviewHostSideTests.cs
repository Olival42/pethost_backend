using FluentAssertions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Domain.UnitTests.Reviews;

/// <summary>O anfitrião avalia o tutor e os pets: o lado, as notas dos pets e quem é o avaliado.</summary>
public sealed class ReviewHostSideTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;

    private static Result<Review> Create(Guid authorId, IReadOnlyList<PetRatingInput>? pets, ReviewParty side = ReviewParty.Host) =>
        Review.Create(ReviewBuilder.CompletedStay(), side, authorId, 5, null, pets, Now);

    [Fact]
    public void Create_Should_KeepThePetRatings_When_TheHostRatesThePetsOfTheStay()
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(ReviewBuilder.PetId, 3, "  Comeu bem.  ")]);

        // Assert
        var review = result.Value!;
        review.AuthorSide.Should().Be(ReviewParty.Host);
        review.SubjectSide.Should().Be(ReviewParty.Owner);
        review.PetRatings.Should().ContainSingle();
        review.PetRatings[0].PetId.Should().Be(ReviewBuilder.PetId);
        review.PetRatings[0].Rating.Should().Be(3);
        review.PetRatings[0].Comment.Should().Be("Comeu bem.");
    }

    [Fact]
    public void Create_Should_ReturnBookingNotFound_When_TheHostIsNotTheHostOfTheStay()
    {
        // Act
        var result = Create(Guid.CreateVersion7(), pets: null);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.BookingNotFound(ReviewBuilder.BookingId));
    }

    [Fact]
    public void Create_Should_ReturnPetsNotAllowed_When_TheOwnerSendsPetRatings()
    {
        // Act
        var result = Create(ReviewBuilder.OwnerId, [new PetRatingInput(ReviewBuilder.PetId, 5, null)], ReviewParty.Owner);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetsNotAllowed);
    }

    [Fact]
    public void Create_Should_ReturnPetNotInStay_When_ThePetIsNotFromTheBooking()
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(Guid.CreateVersion7(), 5, null)]);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetNotInStay);
    }

    [Fact]
    public void Create_Should_ReturnPetDuplicated_When_ThePetIsRatedTwice()
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(ReviewBuilder.PetId, 5, null), new PetRatingInput(ReviewBuilder.PetId, 4, null)]);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetDuplicated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_Should_ReturnPetRatingInvalid_When_ThePetRatingIsOutOfRange(int rating)
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(ReviewBuilder.PetId, rating, null)]);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetRatingInvalid);
    }

    [Fact]
    public void Create_Should_ReturnPetCommentTooLong_When_ThePetCommentIsTooLong()
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(ReviewBuilder.PetId, 5, new string('a', Review.CommentMaxLength + 1))]);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetCommentTooLong);
    }

    [Fact]
    public void Create_Should_ReturnPetCommentInvalidCharacters_When_ThePetCommentHasAControlCharacter()
    {
        // Act
        var result = Create(ReviewBuilder.HostId, [new PetRatingInput(ReviewBuilder.PetId, 5, "a\u0007b")]);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetCommentInvalidCharacters);
    }

    [Fact]
    public void IsWrittenByAndIsAbout_Should_TellTheSides_When_TheHostWrote()
    {
        // Arrange
        var review = new ReviewBuilder().ByHost().Build();

        // Act & Assert
        review.IsWrittenBy(ReviewParty.Host, ReviewBuilder.HostId).Should().BeTrue();
        review.IsWrittenBy(ReviewParty.Owner, ReviewBuilder.OwnerId).Should().BeFalse();
        review.IsAbout(ReviewParty.Owner, ReviewBuilder.OwnerId).Should().BeTrue();
        review.IsAbout(ReviewParty.Host, ReviewBuilder.HostId).Should().BeFalse();
    }

    [Fact]
    public void Update_Should_ReplaceThePets_When_TheHostSendsANewList()
    {
        // Arrange
        var review = new ReviewBuilder().ByHost().Build();

        // Act
        var result = review.Update(null, null, [new PetRatingInput(ReviewBuilder.PetId, 5, null)], [ReviewBuilder.PetId], Now.AddHours(1));

        // Assert
        result.Value.Should().Equal("pets");
        review.PetRatings.Should().ContainSingle(p => p.Rating == 5 && p.Comment == null);
    }

    [Fact]
    public void Update_Should_ChangeNothing_When_ThePetsAreTheSame()
    {
        // Arrange
        var review = new ReviewBuilder().ByHost().Build();

        // Act
        var result = review.Update(null, null, [new PetRatingInput(ReviewBuilder.PetId, 4, "Latiu um pouco à noite.")], [ReviewBuilder.PetId], Now.AddHours(1));

        // Assert
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Update_Should_ReturnPetNotInStay_When_TheStayPetsAreUnknown()
    {
        // Arrange
        var review = new ReviewBuilder().ByHost().Build();

        // Act
        var result = review.Update(null, null, [new PetRatingInput(ReviewBuilder.PetId, 5, null)], stayPetIds: null, Now);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.PetNotInStay);
    }

    [Fact]
    public void ReviewedStay_Should_KnowItsParticipants_When_Asked()
    {
        // Arrange
        var stay = ReviewBuilder.CompletedStay();

        // Act & Assert
        stay.HasParticipant(ReviewParty.Owner, ReviewBuilder.OwnerId).Should().BeTrue();
        stay.HasParticipant(ReviewParty.Host, ReviewBuilder.HostId).Should().BeTrue();
        stay.HasParticipant(ReviewParty.Host, ReviewBuilder.OwnerId).Should().BeFalse();
    }

    [Fact]
    public void ReviewPartyValues_Should_ConvertBothWays_When_Known()
    {
        // Act & Assert
        ReviewPartyValues.ToWire(ReviewParty.Owner).Should().Be("owner");
        ReviewPartyValues.ToWire(ReviewParty.Host).Should().Be("host");
        ReviewPartyValues.FromWire("owner").Should().Be(ReviewParty.Owner);
        ReviewPartyValues.FromWire("host").Should().Be(ReviewParty.Host);
        ReviewPartyValues.Other(ReviewParty.Owner).Should().Be(ReviewParty.Host);
        ReviewPartyValues.Other(ReviewParty.Host).Should().Be(ReviewParty.Owner);
        ReviewPartyValues.All.Should().Equal("owner", "host");
    }

    [Fact]
    public void ReviewPartyValues_Should_Throw_When_TheValueIsUnknown()
    {
        // Act
        var act = () => ReviewPartyValues.FromWire("admin");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
