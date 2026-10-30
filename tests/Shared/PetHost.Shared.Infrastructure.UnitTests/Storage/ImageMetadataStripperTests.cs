using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Storage;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Storage;

public sealed class ImageMetadataStripperTests
{
    private static readonly byte[] Soi = [0xFF, 0xD8];
    private static readonly byte[] Eoi = [0xFF, 0xD9];

    // Dados comprimidos com um FF escapado (FF 00) e um reinício (FF D0) no meio.
    private static readonly byte[] ScanData = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];

    private static readonly byte[] Jfif = Segment(0xE0, [.. "JFIF\0"u8, 0x01, 0x02, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
    private static readonly byte[] Dqt = Segment(0xDB, [0x00, .. Enumerable.Repeat((byte)0x10, 64)]);
    private static readonly byte[] Sos = Segment(0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);

    [Fact]
    public void Strip_Should_RemoveExifAndKeepOnlyTheOrientation_When_JpegHasGpsData()
    {
        // Arrange
        byte[] jpeg = [.. Soi, .. Jfif, .. ExifSegment(orientation: 6, bigEndian: true, extra: "GPS -23.5505 -46.6333"), .. Dqt, .. Sos, .. ScanData, .. Eoi];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        Contains(result, "GPS"u8).Should().BeFalse();
        result.Should().Equal([.. Soi, .. Jfif, .. MinimalExif(6), .. Dqt, .. Sos, .. ScanData, .. Eoi]);
    }

    [Fact]
    public void Strip_Should_ReadTheOrientation_When_ExifIsLittleEndian()
    {
        // Arrange
        byte[] jpeg = [.. Soi, .. ExifSegment(orientation: 8, bigEndian: false), .. Dqt, .. Sos, .. ScanData, .. Eoi];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert — sem APP0, o EXIF mínimo vai logo depois do SOI.
        result.Should().Equal([.. Soi, .. MinimalExif(8), .. Dqt, .. Sos, .. ScanData, .. Eoi]);
    }

    [Fact]
    public void Strip_Should_DropExifEntirely_When_OrientationIsNormal()
    {
        // Arrange
        byte[] jpeg = [.. Soi, .. Jfif, .. ExifSegment(orientation: 1, bigEndian: true), .. Dqt, .. Sos, .. ScanData, .. Eoi];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        result.Should().Equal([.. Soi, .. Jfif, .. Dqt, .. Sos, .. ScanData, .. Eoi]);
    }

    [Fact]
    public void Strip_Should_KeepColorProfileAndAdobeBlock_And_DropOtherMetadata()
    {
        // Arrange
        var icc = Segment(0xE2, [.. "ICC_PROFILE\0"u8, 0x01, 0x01, 0xAA]);
        var mpf = Segment(0xE2, [.. "MPF\0"u8, 0x01]);
        var xmp = Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta/>"u8]);
        var iptc = Segment(0xED, [.. "Photoshop 3.0\0"u8, 0x01]);
        var adobe = Segment(0xEE, [.. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x01]);
        var comment = Segment(0xFE, [.. "Tirada na casa da Ana"u8]);
        byte[] jpeg = [.. Soi, .. Jfif, .. icc, .. mpf, .. xmp, .. iptc, .. adobe, .. comment, .. Dqt, .. Sos, .. ScanData, .. Eoi];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        result.Should().Equal([.. Soi, .. Jfif, .. icc, .. adobe, .. Dqt, .. Sos, .. ScanData, .. Eoi]);
    }

    [Fact]
    public void Strip_Should_DiscardEverythingAfterEndOfImage()
    {
        // Arrange — celulares anexam outras imagens (com o próprio EXIF) depois do EOI.
        byte[] trailing = [.. Soi, .. ExifSegment(orientation: 1, bigEndian: true, extra: "GPS"), .. Eoi];
        byte[] jpeg = [.. Soi, .. Dqt, .. Sos, .. ScanData, .. Eoi, .. trailing];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        result.Should().Equal([.. Soi, .. Dqt, .. Sos, .. ScanData, .. Eoi]);
    }

    [Fact]
    public void Strip_Should_KeepSegmentsBetweenScans_When_JpegIsProgressive()
    {
        // Arrange
        var dht = Segment(0xC4, [0x00, .. Enumerable.Repeat((byte)0x01, 16), 0x00]);
        byte[] jpeg = [.. Soi, .. Dqt, .. Sos, .. ScanData, .. dht, .. Segment(0xFE, [.. "x"u8]), .. Sos, .. ScanData, .. Eoi];

        // Act
        var result = Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        result.Should().Equal([.. Soi, .. Dqt, .. Sos, .. ScanData, .. dht, .. Sos, .. ScanData, .. Eoi]);
    }

    [Theory]
    [MemberData(nameof(BrokenJpegs))]
    public void Strip_Should_ReturnNull_When_JpegStructureIsBroken(byte[] jpeg)
    {
        // Act
        var result = ImageMetadataStripper.Strip(jpeg, ImageFormat.Jpeg);

        // Assert
        result.Should().BeNull();
    }

    public static TheoryData<byte[]> BrokenJpegs() => new()
    {
        { [.. Soi, .. Dqt, .. Sos, .. ScanData] },                    // cortado: sem EOI
        { [.. Soi, 0xFF, 0xDB, 0x00, 0x50, 0x00] },                    // tamanho maior que o arquivo
        { [.. Soi, 0x00, 0x00, 0x00] },                                // lixo no lugar de marcador
        { [.. Soi, .. Dqt] },                                          // acaba antes da imagem
    };

    [Fact]
    public void Strip_Should_RemoveTextAndExifChunks_When_ImageIsPng()
    {
        // Arrange
        var ihdr = Chunk("IHDR", new byte[13]);
        var idat = Chunk("IDAT", [0x78, 0x9C, 0x01]);
        var iend = Chunk("IEND", []);
        byte[] png =
        [
            .. PngSignature, .. ihdr,
            .. Chunk("eXIf", [.. "MM"u8, 0x00, 0x2A]),
            .. Chunk("tEXt", [.. "Author\0Ana"u8]),
            .. Chunk("iTXt", [.. "GPS\0"u8]),
            .. Chunk("tIME", new byte[7]),
            .. idat, .. Chunk("zTXt", [.. "c\0\0x"u8]), .. iend,
            .. "lixo depois do fim"u8,
        ];

        // Act
        var result = Strip(png, ImageFormat.Png);

        // Assert
        result.Should().Equal([.. PngSignature, .. ihdr, .. idat, .. iend]);
    }

    [Fact]
    public void Strip_Should_ReturnNull_When_PngHasNoEnd()
    {
        // Act
        var result = ImageMetadataStripper.Strip([.. PngSignature, .. Chunk("IHDR", new byte[13])], ImageFormat.Png);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Strip_Should_RemoveExifAndXmpChunks_And_ClearTheirFlags_When_ImageIsWebp()
    {
        // Arrange — VP8X com as flags ICC (0x20), EXIF (0x08) e XMP (0x04).
        var vp8x = RiffChunk("VP8X", [0x2C, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x09, 0x00, 0x00]);
        var iccp = RiffChunk("ICCP", [0x01, 0x02]);
        var vp8 = RiffChunk("VP8 ", [0x10, 0x20, 0x30]);   // tamanho ímpar: leva preenchimento
        byte[] chunks = [.. vp8x, .. iccp, .. vp8, .. RiffChunk("EXIF", [.. "MM\0*GPS"u8]), .. RiffChunk("XMP ", [.. "<x/>"u8])];
        var webp = Riff(chunks);

        // Act
        var result = Strip(webp, ImageFormat.Webp);

        // Assert
        var expectedVp8x = vp8x.ToArray();
        expectedVp8x[8] = 0x20;
        result.Should().Equal(Riff([.. expectedVp8x, .. iccp, .. vp8]));
    }

    [Fact]
    public void Strip_Should_ReturnNull_When_WebpChunkIsLargerThanTheFile()
    {
        // Arrange
        byte[] chunks = [.. "VP8 "u8, 0xFF, 0x00, 0x00, 0x00, 0x01];

        // Act
        var result = ImageMetadataStripper.Strip(Riff(chunks), ImageFormat.Webp);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_Should_ReturnTheImageWithoutMetadata()
    {
        // Arrange
        byte[] jpeg = [.. Soi, .. ExifSegment(orientation: 1, bigEndian: true, extra: "GPS"), .. Dqt, .. Sos, .. ScanData, .. Eoi];
        using var content = new MemoryStream(jpeg);

        // Act
        var result = await ImageUploadReader.ReadAsync(new ImageUpload(content, "foto.jpg", "image/jpeg"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        using var image = result.Value!;
        image.Buffer.ToArray().Should().Equal([.. Soi, .. Dqt, .. Sos, .. ScanData, .. Eoi]);
        image.Format.Should().Be(ImageFormat.Jpeg);
    }

    [Fact]
    public async Task ReadAsync_Should_FailWithCorrupted_When_JpegIsTruncated()
    {
        // Arrange
        byte[] jpeg = [.. Soi, .. Dqt, .. Sos, .. ScanData];
        using var content = new MemoryStream(jpeg);

        // Act
        var result = await ImageUploadReader.ReadAsync(new ImageUpload(content, "foto.jpg", "image/jpeg"), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.FirstError.Should().Be(ImageErrors.FileCorrupted);
    }

    // ------------------------------------------------------------ helpers

    private static byte[] Strip(byte[] image, ImageFormat format)
    {
        using var stripped = ImageMetadataStripper.Strip(image, format);
        stripped.Should().NotBeNull();
        return stripped!.ToArray();
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = (ushort)(payload.Length + 2);
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    /// <summary>APP1 com EXIF: IFD0 com uma tag qualquer antes da orientação, e texto extra no fim.</summary>
    private static byte[] ExifSegment(ushort orientation, bool bigEndian, string extra = "")
    {
        var tiff = new List<byte>();
        void U16(ushort v) => tiff.AddRange(bigEndian ? [(byte)(v >> 8), (byte)v] : [(byte)v, (byte)(v >> 8)]);
        void U32(uint v)
        {
            var bytes = new byte[4];
            if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(bytes, v);
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes, v);
            tiff.AddRange(bytes);
        }

        tiff.AddRange(bigEndian ? "MM"u8.ToArray() : "II"u8.ToArray());
        U16(42);
        U32(8);
        U16(2);
        U16(0x010F); U16(2); U32(4); U32(0x41424300);     // Make, ASCII "ABC"
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U32(0);
        tiff.AddRange(Encoding.ASCII.GetBytes(extra));

        return Segment(0xE1, [.. "Exif\0\0"u8, .. tiff]);
    }

    private static byte[] MinimalExif(byte orientation) =>
        Segment(0xE1, [.. "Exif\0\0"u8, .. "MM"u8, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01, 0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, orientation, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Chunk PNG. O CRC não é conferido pelo removedor: zeros bastam.</summary>
    private static byte[] Chunk(string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        return [.. length, .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0];
    }

    private static byte[] RiffChunk(string fourCc, byte[] data)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        byte[] chunk = [.. Encoding.ASCII.GetBytes(fourCc), .. size, .. data];
        return data.Length % 2 == 1 ? [.. chunk, 0x00] : chunk;
    }

    private static byte[] Riff(byte[] chunks)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(chunks.Length + 4));
        return [.. "RIFF"u8, .. size, .. "WEBP"u8, .. chunks];
    }
}
