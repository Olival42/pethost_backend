using FluentAssertions;
using PetHost.Shared.Infrastructure.Storage;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Storage;

public sealed class ImageSignatureTests
{
    [Fact]
    public void Detect_Should_RecognizeJpeg_When_HeaderStartsWithFfD8Ff()
    {
        // Act
        var format = ImageSignature.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);

        // Assert
        format.Should().Be(ImageFormat.Jpeg);
        format!.Extension.Should().Be(".jpg");
    }

    [Fact]
    public void Detect_Should_RecognizePng_When_HeaderHasThePngSignature()
    {
        // Act
        var format = ImageSignature.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]);

        // Assert
        format.Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void Detect_Should_RecognizeWebp_When_HeaderIsRiffWebp()
    {
        // Arrange — "RIFF", tamanho (4 bytes), "WEBP"
        byte[] header = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8];

        // Act
        var format = ImageSignature.Detect(header);

        // Assert
        format.Should().Be(ImageFormat.Webp);
        format!.ContentType.Should().Be("image/webp");
    }

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })] // GIF
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })] // PDF
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })] // executável do Windows
    [InlineData(new byte[] { 0xFF, 0xD8 })] // JPEG cortado
    [InlineData(new byte[0])]
    public void Detect_Should_ReturnNull_When_ContentIsNotAnAcceptedImage(byte[] header)
    {
        // Act
        var format = ImageSignature.Detect(header);

        // Assert
        format.Should().BeNull();
    }

    [Fact]
    public void Detect_Should_RejectRiffThatIsNotWebp()
    {
        // Arrange — WAV também começa com RIFF.
        byte[] header = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8];

        // Act
        var format = ImageSignature.Detect(header);

        // Assert
        format.Should().BeNull();
    }
}
