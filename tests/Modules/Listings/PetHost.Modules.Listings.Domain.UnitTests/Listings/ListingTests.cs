using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class ListingTests
{
    private static readonly DateTimeOffset Now = ListingBuilder.DefaultNow;
    private static readonly DateTimeOffset Later = Now.AddHours(2);

    private readonly ListingBuilder _builder = new();

    // ----- cadastro e limite PF/PJ -----

    [Fact]
    public void Create_Should_StartActive_When_IndividualHasNoListing()
    {
        // Arrange
        var hostId = Guid.CreateVersion7();

        // Act
        var result = Listing.Create(new ListingHost(hostId, false), 0, _builder.BuildDetails(), _builder.BuildAddress(), _builder.BuildLocation(), Now);

        // Assert
        result.Value!.HostId.Should().Be(hostId);
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.Photos.Should().BeEmpty();
    }

    [Fact]
    public void Create_Should_ReturnLimitReached_When_IndividualAlreadyHasAListing()
    {
        // Act
        var result = Listing.Create(new ListingHost(Guid.CreateVersion7(), false), 1, _builder.BuildDetails(), _builder.BuildAddress(), _builder.BuildLocation(), Now);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.LimitReached);
    }

    [Fact]
    public void Create_Should_AllowManyListings_When_HostIsCompany()
    {
        // Act
        var result = Listing.Create(new ListingHost(Guid.CreateVersion7(), true), 7, _builder.BuildDetails(), _builder.BuildAddress(), _builder.BuildLocation(), Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Throw_When_HostIdIsEmpty()
    {
        // Act
        var act = () => Listing.Create(new ListingHost(Guid.Empty, false), 0, _builder.BuildDetails(), _builder.BuildAddress(), _builder.BuildLocation(), Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    // ----- alterações -----

    [Fact]
    public void UpdateDetails_Should_ReturnFalseAndKeepUpdatedAt_When_NothingChanged()
    {
        // Arrange
        var listing = _builder.Build();

        // Act
        var changed = listing.UpdateDetails(_builder.BuildDetails(), Later);

        // Assert
        changed.Should().BeFalse();
        listing.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void UpdateDetails_Should_ChangeAndTouchUpdatedAt_When_RateChanges()
    {
        // Arrange
        var listing = _builder.Build();
        var cheaper = new ListingBuilder().WithDetails(d => d with { NightlyRate = 45m }).BuildDetails();

        // Act
        var changed = listing.UpdateDetails(cheaper, Later);

        // Assert
        changed.Should().BeTrue();
        listing.Details.NightlyRate.Should().Be(45m);
        listing.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void Relocate_Should_MoveAddressAndLocationTogether_When_AddressChanges()
    {
        // Arrange
        var listing = _builder.Build();
        var other = new ListingBuilder().WithAddress(a => a with { Neighborhood = "Zona 5" }).At(-23.41m, -51.92m);

        // Act
        var changed = listing.Relocate(other.BuildAddress(), other.BuildLocation(), Later);

        // Assert
        changed.Should().BeTrue();
        listing.Address.Neighborhood.Should().Be("Zona 5");
        listing.Location.Latitude.Should().Be(-23.41m);
        listing.Location.DistanceKmTo(listing.ApproximateLocation).Should().BeLessThan(0.5);
    }

    [Fact]
    public void Create_Should_DrawAnApproximateLocationNearTheHome()
    {
        // Act
        var listing = _builder.Build();

        // Assert
        listing.ApproximateLocation.Should().NotBe(listing.Location);
        listing.Location.DistanceKmTo(listing.ApproximateLocation).Should().BeLessThan(0.5);
    }

    [Fact]
    public void Relocate_Should_KeepTheApproximateLocation_When_OnlyTheAddressTextChanges()
    {
        // Arrange — complemento novo, mesma coordenada: sortear de novo deixaria cercar a casa pela média.
        var listing = _builder.Build();
        var approximate = listing.ApproximateLocation;
        var sameSpot = new ListingBuilder().WithAddress(a => a with { Complement = "Fundos" });

        // Act
        var changed = listing.Relocate(sameSpot.BuildAddress(), _builder.BuildLocation(), Later);

        // Assert
        changed.Should().BeTrue();
        listing.ApproximateLocation.Should().Be(approximate);
    }

    [Fact]
    public void Relocate_Should_ReturnFalse_When_AddressAndLocationAreTheSame()
    {
        // Arrange
        var listing = _builder.Build();

        // Act & Assert
        listing.Relocate(_builder.BuildAddress(), _builder.BuildLocation(), Later).Should().BeFalse();
    }

    // ----- ativo/inativo -----

    [Fact]
    public void Deactivate_Should_RecordWhen_And_Reactivate_Should_ClearIt()
    {
        // Arrange
        var listing = _builder.Build();

        // Act
        listing.Deactivate(Later);
        var deactivatedAt = listing.DeactivatedAt;
        listing.Reactivate(Later.AddDays(1));

        // Assert
        deactivatedAt.Should().Be(Later);
        listing.IsActive.Should().BeTrue();
        listing.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public void Deactivate_Should_KeepTheFirstDate_When_CalledTwice()
    {
        // Arrange
        var listing = _builder.Build();
        listing.Deactivate(Later);

        // Act
        listing.Deactivate(Later.AddDays(1));

        // Assert
        listing.DeactivatedAt.Should().Be(Later);
    }

    // ----- fotos -----

    [Fact]
    public void AddPhoto_Should_AppendInOrder_When_ThereIsRoom()
    {
        // Arrange
        var listing = _builder.Build();

        // Act
        listing.AddPhoto("https://cdn.pethost.test/1.jpg", Hash('1'), Later);
        listing.AddPhoto("https://cdn.pethost.test/2.jpg", Hash('2'), Later);

        // Assert
        listing.Photos.Select(p => p.Position).Should().Equal(1, 2);
        listing.Photos[0].Url.Should().EndWith("1.jpg");
    }

    [Fact]
    public void AddPhoto_Should_ReturnLimitReached_When_ListingHasFivePhotos()
    {
        // Arrange
        var listing = WithPhotos(5);

        // Act
        var result = listing.AddPhoto("https://cdn.pethost.test/6.jpg", Hash('6'), Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoLimitReached);
        listing.CanAddPhoto.Should().BeFalse();
    }

    [Fact]
    public void AddPhoto_Should_ReturnAlreadyExists_When_ImageIsRepeated()
    {
        // Arrange
        var listing = WithPhotos(1);

        // Act
        var result = listing.AddPhoto("https://cdn.pethost.test/again.jpg", Hash('1'), Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoAlreadyExists);
    }

    [Fact]
    public void AddPhoto_Should_RejectUrl_When_ItIsNotHttp()
    {
        // Arrange
        var listing = _builder.Build();

        // Act
        var result = listing.AddPhoto("javascript:alert(1)", Hash('1'), Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoUrlInvalid);
    }

    [Fact]
    public void RemovePhoto_Should_MoveTheNextPhotosUp_When_APhotoInTheMiddleIsRemoved()
    {
        // Arrange
        var listing = WithPhotos(3);
        var middle = listing.Photos[1];

        // Act
        listing.RemovePhoto(middle.Id, Later);

        // Assert
        listing.Photos.Select(p => (p.Url, p.Position)).Should().Equal(
            ("https://cdn.pethost.test/1.jpg", 1),
            ("https://cdn.pethost.test/3.jpg", 2));
    }

    [Fact]
    public void RemovePhoto_Should_ReturnNotFound_When_PhotoIsFromAnotherListing()
    {
        // Arrange
        var listing = WithPhotos(1);
        var unknown = ListingPhotoId.New();

        // Act
        var result = listing.RemovePhoto(unknown, Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoNotFound(unknown));
    }

    [Fact]
    public void ReplacePhoto_Should_KeepIdAndPosition_And_ReturnThePreviousUrl()
    {
        // Arrange
        var listing = WithPhotos(2);
        var second = listing.Photos[1];

        // Act
        var previous = listing.ReplacePhoto(second.Id, "https://cdn.pethost.test/new.jpg", Hash('n'), Later);

        // Assert
        previous.Value.Should().Be("https://cdn.pethost.test/2.jpg");
        listing.Photos[1].Id.Should().Be(second.Id);
        listing.Photos[1].Url.Should().Be("https://cdn.pethost.test/new.jpg");
    }

    [Fact]
    public void ReplacePhoto_Should_ReturnAlreadyExists_When_ImageIsInAnotherPhoto()
    {
        // Arrange
        var listing = WithPhotos(2);

        // Act
        var result = listing.ReplacePhoto(listing.Photos[1].Id, "https://cdn.pethost.test/x.jpg", Hash('1'), Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoAlreadyExists);
    }

    [Fact]
    public void ReplacePhoto_Should_ReturnNotFound_When_PhotoDoesNotExist()
    {
        // Arrange
        var listing = WithPhotos(1);
        var unknown = ListingPhotoId.New();

        // Act
        var result = listing.ReplacePhoto(unknown, "https://cdn.pethost.test/x.jpg", Hash('x'), Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoNotFound(unknown));
    }

    [Fact]
    public void ReorderPhotos_Should_PutPhotosInTheGivenOrder_When_ListHasEveryPhoto()
    {
        // Arrange
        var listing = WithPhotos(3);
        var (first, second, third) = (listing.Photos[0], listing.Photos[1], listing.Photos[2]);

        // Act
        var result = listing.ReorderPhotos([third.Id, first.Id, second.Id], Later);

        // Assert
        result.Value.Should().BeTrue();
        listing.Photos.Select(p => p.Id).Should().Equal(third.Id, first.Id, second.Id);
        listing.Photos.Select(p => p.Position).Should().Equal(1, 2, 3);
        listing.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void ReorderPhotos_Should_ReturnFalse_When_OrderIsTheSame()
    {
        // Arrange
        var listing = WithPhotos(2);
        var updatedAt = listing.UpdatedAt;

        // Act
        var result = listing.ReorderPhotos([.. listing.Photos.Select(p => p.Id)], Later.AddDays(1));

        // Assert
        result.Value.Should().BeFalse();
        listing.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ReorderPhotos_Should_ReturnOrderInvalid_When_APhotoIsMissing()
    {
        // Arrange
        var listing = WithPhotos(3);

        // Act
        var result = listing.ReorderPhotos([listing.Photos[0].Id, listing.Photos[1].Id], Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoOrderInvalid);
    }

    [Fact]
    public void ReorderPhotos_Should_ReturnOrderInvalid_When_APhotoIsRepeated()
    {
        // Arrange
        var listing = WithPhotos(2);

        // Act
        var result = listing.ReorderPhotos([listing.Photos[0].Id, listing.Photos[0].Id], Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoOrderInvalid);
    }

    [Fact]
    public void ReorderPhotos_Should_ReturnPhotoNotFound_When_APhotoIsFromAnotherListing()
    {
        // Arrange
        var listing = WithPhotos(2);
        var unknown = ListingPhotoId.New();

        // Act
        var result = listing.ReorderPhotos([listing.Photos[0].Id, unknown], Later);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoNotFound(unknown));
        listing.Photos.Select(p => p.Position).Should().Equal(1, 2);
    }

    private Listing WithPhotos(int count)
    {
        var listing = _builder.Build();
        foreach (var index in Enumerable.Range(1, count))
            listing.AddPhoto($"https://cdn.pethost.test/{index}.jpg", Hash((char)('0' + index)), Now);

        return listing;
    }

    private static string Hash(char seed) => new(seed, 64);
}
