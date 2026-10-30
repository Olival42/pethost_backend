using FluentAssertions;
using Moq;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Storage;

public sealed class ImageReplacementTests
{
    private const string NewUrl = "https://img.pethost.test/pets/new.png";
    private const string NewHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string OldUrl = "https://img.pethost.test/pets/old.png";

    private static readonly Error SaveFailed = new("PET_SAVE_FAILED", "Could not save.");

    private readonly Mock<IImageStorage> _storage = new();
    private readonly ImageUpload _image = new(new MemoryStream([1, 2, 3]), "a.png", "image/png");

    public ImageReplacementTests()
    {
        _storage
            .Setup(s => s.SaveAsync(_image, "pets", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Success(new StoredImage(NewUrl, NewHash)));
    }

    [Fact]
    public async Task ReplaceAsync_Should_SaveTheNewUrlAndDeleteTheOldImage_When_EverythingWorks()
    {
        // Arrange
        string? saved = null;

        // Act
        var result = await ImageReplacement.ReplaceAsync(
            _storage.Object,
            _image,
            "pets",
            (image, _) =>
            {
                saved = image.Url;
                return Task.FromResult(Result<ImageChange<string>>.Success(new ImageChange<string>("ok", OldUrl)));
            },
            CancellationToken.None);

        // Assert
        result.Value.Should().Be("ok");
        saved.Should().Be(NewUrl);
        _storage.Verify(s => s.DeleteAsync(OldUrl, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(NewUrl, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceAsync_Should_DeleteTheNewImage_When_SavingTheEntityFails()
    {
        // Act
        var result = await ImageReplacement.ReplaceAsync(
            _storage.Object,
            _image,
            "pets",
            (_, _) => Task.FromResult(Result<ImageChange<string>>.Failure(SaveFailed)),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(SaveFailed);
        _storage.Verify(s => s.DeleteAsync(NewUrl, It.IsAny<CancellationToken>()), Times.Once, "o arquivo novo não fica órfão");
        _storage.Verify(s => s.DeleteAsync(OldUrl, It.IsAny<CancellationToken>()), Times.Never, "a foto antiga continua valendo");
    }

    [Fact]
    public async Task ReplaceAsync_Should_DeleteTheNewImageAndRethrow_When_SavingTheEntityThrows()
    {
        // Act
        var act = () => ImageReplacement.ReplaceAsync<string>(
            _storage.Object,
            _image,
            "pets",
            (_, _) => throw new InvalidOperationException("banco fora"),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _storage.Verify(s => s.DeleteAsync(NewUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplaceAsync_Should_NotTouchTheEntity_When_ImageIsInvalid()
    {
        // Arrange
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Failure(ImageErrors.UnsupportedFormat));
        var applied = false;

        // Act
        var result = await ImageReplacement.ReplaceAsync(
            _storage.Object,
            _image,
            "pets",
            (_, _) =>
            {
                applied = true;
                return Task.FromResult(Result<ImageChange<string>>.Success(new ImageChange<string>("ok", null)));
            },
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ImageErrors.UnsupportedFormat);
        applied.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_Should_DeleteThePreviousImage_When_EntityIsCleared()
    {
        // Act
        var result = await ImageReplacement.RemoveAsync(
            _storage.Object,
            _ => Task.FromResult(Result<ImageChange<string>>.Success(new ImageChange<string>("ok", OldUrl))),
            CancellationToken.None);

        // Assert
        result.Value.Should().Be("ok");
        _storage.Verify(s => s.DeleteAsync(OldUrl, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
