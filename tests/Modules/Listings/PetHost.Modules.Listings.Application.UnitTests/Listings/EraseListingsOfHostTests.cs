using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Listings.EraseListingsOfHost;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>A conta do anfitrião foi excluída (LGPD): os cantinhos saem do ar sem fotos nem endereço exato.</summary>
public sealed class EraseListingsOfHostTests
{
    private static readonly DateTimeOffset Later = ListingBuilder.DefaultNow.AddDays(10);
    private static readonly Guid HostId = Guid.CreateVersion7();

    private readonly Mock<IListingRepository> _listings = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IImageStorage> _images = new();
    private readonly Listing _listing = new ListingBuilder().Build();
    private readonly List<string> _calls = [];

    public EraseListingsOfHostTests()
    {
        _listing.AddPhoto("https://cdn.pethost.test/1.jpg", new string('1', 64), ListingBuilder.DefaultNow);

        _listings.Setup(l => l.ListByHostForUpdateAsync(HostId, It.IsAny<CancellationToken>())).ReturnsAsync([_listing]);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("save")).ReturnsAsync(1);
        _images
            .Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("photo"))
            .Returns(Task.CompletedTask);
    }

    private EraseListingsOfHostCommandHandler Handler() => new(_listings.Object, _unitOfWork.Object, _images.Object, new FakeTimeProvider(Later));

    [Fact]
    public async Task HandleAsync_Should_EraseTheListingsAndThenTheirPhotos_When_HostHasListings()
    {
        // Act
        var result = await Handler().HandleAsync(new EraseListingsOfHostCommand(HostId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(1);
        _listing.IsActive.Should().BeFalse();
        _listing.Photos.Should().BeEmpty();
        _listing.Address.Street.Should().Be(ListingAddress.RedactedStreet);
        _calls.Should().Equal(["save", "photo"], "a foto sai do bucket só depois que o banco gravou");
        _images.Verify(s => s.DeleteAsync("https://cdn.pethost.test/1.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveNothing_When_HostHasNoListing()
    {
        // Arrange
        _listings.Setup(l => l.ListByHostForUpdateAsync(HostId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await Handler().HandleAsync(new EraseListingsOfHostCommand(HostId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_Should_RequireTheHostId_When_Empty()
    {
        // Act
        var result = new EraseListingsOfHostCommandValidator().Validate(new EraseListingsOfHostCommand(Guid.Empty));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "HostId");
    }
}
