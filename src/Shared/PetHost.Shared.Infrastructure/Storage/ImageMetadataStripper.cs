using System.Buffers.Binary;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Tira os metadados da imagem antes de gravar: EXIF (GPS, modelo do celular, data e hora,
/// número de série), XMP, IPTC, comentários e miniaturas. Foto de celular costuma levar a
/// localização exata de onde foi tirada — na casa de quem enviou.
/// </summary>
/// <remarks>
/// Remove só os blocos de metadados e copia o resto byte a byte: a imagem não é decodificada
/// nem recomprimida, então não perde qualidade e o hash de foto repetida continua estável.
/// Fica o que muda como a imagem é exibida: perfil de cor (ICC), o bloco Adobe do JPEG e,
/// no JPEG, a <b>orientação</b> — regravada num EXIF mínimo só com ela, senão foto de celular
/// em pé apareceria deitada. Tudo depois do fim da imagem (as imagens extras que alguns
/// celulares anexam, cada uma com o próprio EXIF) é descartado.
/// </remarks>
internal static class ImageMetadataStripper
{
    /// <summary>A imagem sem metadados, ou <c>null</c> se a estrutura do arquivo estiver corrompida.</summary>
    public static MemoryStream? Strip(ReadOnlySpan<byte> image, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (format == ImageFormat.Jpeg)
            return StripJpeg(image);
        if (format == ImageFormat.Png)
            return StripPng(image);
        if (format == ImageFormat.Webp)
            return StripWebp(image);

        return null;
    }

    // ---------------------------------------------------------------- JPEG

    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte App0 = 0xE0;
    private const byte App1 = 0xE1;
    private const byte App2 = 0xE2;
    private const byte App14 = 0xEE;
    private const byte Comment = 0xFE;

    private static MemoryStream? StripJpeg(ReadOnlySpan<byte> src)
    {
        if (src.Length < 4 || src[0] != 0xFF || src[1] != 0xD8)
            return null;

        // Trechos mantidos, na ordem. O EXIF mínimo entra depois do APP0 (JFIF), que tem de vir primeiro.
        var kept = new List<(int Start, int Length)>();
        var exifInsertAt = 0;
        ushort orientation = 1;
        var pos = 2;

        while (true)
        {
            if (pos >= src.Length || src[pos] != 0xFF)
                return null;

            while (pos < src.Length && src[pos] == 0xFF)
                pos++;
            if (pos >= src.Length)
                return null;

            var marker = src[pos++];
            var markerStart = pos - 2;

            if (marker == EndOfImage)
            {
                kept.Add((markerStart, 2));
                break;
            }

            // Marcadores sem tamanho: RST0–RST7 e TEM.
            if (marker is (>= 0xD0 and <= 0xD7) or 0x01)
            {
                kept.Add((markerStart, 2));
                continue;
            }

            if (pos + 2 > src.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(src[pos..]);
            if (length < 2 || pos + length > src.Length)
                return null;

            var payload = src.Slice(pos + 2, length - 2);
            var segmentEnd = pos + length;

            if (marker == StartOfScan)
            {
                // Cabeçalho do scan + dados comprimidos, até o próximo marcador de verdade
                // (FF 00 é byte FF escapado; FF D0–D7 são reinícios dentro dos dados).
                pos = segmentEnd;
                while (pos + 1 < src.Length)
                {
                    if (src[pos] != 0xFF)
                    {
                        pos++;
                        continue;
                    }

                    var next = src[pos + 1];
                    if (next == 0x00 || next is >= 0xD0 and <= 0xD7)
                        pos += 2;
                    else if (next == 0xFF)
                        pos++;
                    else
                        break;
                }

                if (pos + 1 >= src.Length)
                    return null;   // Sem fim de imagem: arquivo cortado.

                kept.Add((markerStart, pos - markerStart));
                continue;
            }

            if (marker == App1 && orientation == 1)
                orientation = ReadExifOrientation(payload);

            if (KeepJpegSegment(marker, payload))
            {
                kept.Add((markerStart, segmentEnd - markerStart));
                if (marker == App0 && exifInsertAt == kept.Count - 1)
                    exifInsertAt = kept.Count;
            }

            pos = segmentEnd;
        }

        var output = new MemoryStream(src.Length);
        output.Write(src[..2]);

        for (var i = 0; i < kept.Count; i++)
        {
            if (i == exifInsertAt && orientation != 1)
                output.Write(MinimalExif(orientation));

            output.Write(src.Slice(kept[i].Start, kept[i].Length));
        }

        return output;
    }

    /// <summary>
    /// Mantém o que a imagem precisa para ser decodificada e exibida certo. Sai: APP1 (EXIF,
    /// XMP), APP2 que não é perfil de cor (FlashPix, MPF), APP3–APP13 e APP15 (dados de
    /// fabricante, IPTC do Photoshop), comentários e o APP0 de miniatura (JFXX).
    /// </summary>
    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        App0 => payload.StartsWith("JFIF\0"u8),
        App2 => payload.StartsWith("ICC_PROFILE\0"u8),
        App14 => true,
        Comment => false,
        >= App0 and <= 0xEF => false,
        _ => true,
    };

    /// <summary>A orientação (tag 0x0112) do IFD0 do EXIF, ou 1 (normal) se não houver ou não der para ler.</summary>
    private static ushort ReadExifOrientation(ReadOnlySpan<byte> payload)
    {
        if (!payload.StartsWith("Exif\0\0"u8))
            return 1;

        var tiff = payload[6..];
        if (tiff.Length < 8)
            return 1;

        bool bigEndian;
        if (tiff.StartsWith("MM"u8))
            bigEndian = true;
        else if (tiff.StartsWith("II"u8))
            bigEndian = false;
        else
            return 1;

        if (ReadUInt16(tiff[2..], bigEndian) != 42)
            return 1;

        var ifd = ReadUInt32(tiff[4..], bigEndian);
        if (ifd > (uint)tiff.Length - 2)
            return 1;

        var count = ReadUInt16(tiff[(int)ifd..], bigEndian);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
                return 1;

            if (ReadUInt16(tiff[entry..], bigEndian) != 0x0112)
                continue;

            // Tipo SHORT (3), um valor, guardado nos primeiros 2 bytes do campo de valor.
            if (ReadUInt16(tiff[(entry + 2)..], bigEndian) != 3)
                return 1;

            var value = ReadUInt16(tiff[(entry + 8)..], bigEndian);
            return value is >= 1 and <= 8 ? value : (ushort)1;
        }

        return 1;
    }

    /// <summary>APP1 com um EXIF que só tem a orientação (TIFF big-endian, um IFD de uma entrada).</summary>
    private static byte[] MinimalExif(ushort orientation) =>
    [
        0xFF, App1, 0x00, 0x22,                 // marcador e tamanho (34)
        .. "Exif\0\0"u8,
        .. "MM"u8, 0x00, 0x2A,                  // TIFF big-endian
        0x00, 0x00, 0x00, 0x08,                 // IFD0 logo depois do cabeçalho
        0x00, 0x01,                             // uma entrada
        0x01, 0x12, 0x00, 0x03,                 // Orientation, SHORT
        0x00, 0x00, 0x00, 0x01,                 // um valor
        (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,                 // sem próximo IFD
    ];

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);

    private static uint ReadUInt32(ReadOnlySpan<byte> span, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);

    // ---------------------------------------------------------------- PNG

    private const int PngSignatureLength = 8;

    /// <summary>
    /// Sai: <c>eXIf</c>, os textos (<c>tEXt</c>, <c>zTXt</c>, <c>iTXt</c> — autor, software,
    /// às vezes o EXIF inteiro) e <c>tIME</c>. A orientação de PNG não é mantida: câmera e
    /// celular gravam foto em JPEG.
    /// </summary>
    private static MemoryStream? StripPng(ReadOnlySpan<byte> src)
    {
        if (src.Length < PngSignatureLength)
            return null;

        var output = new MemoryStream(src.Length);
        output.Write(src[..PngSignatureLength]);

        var pos = PngSignatureLength;
        while (true)
        {
            // Tamanho (4) + tipo (4) + dados + CRC (4).
            if (pos + 12 > src.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt32BigEndian(src[pos..]);
            if (length > (uint)(src.Length - pos - 12))
                return null;

            var type = src.Slice(pos + 4, 4);
            var chunk = src.Slice(pos, 12 + (int)length);

            if (!IsPngMetadata(type))
                output.Write(chunk);

            pos += chunk.Length;

            if (type.SequenceEqual("IEND"u8))
                return output;
        }
    }

    private static bool IsPngMetadata(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("eXIf"u8)
        || type.SequenceEqual("tEXt"u8)
        || type.SequenceEqual("zTXt"u8)
        || type.SequenceEqual("iTXt"u8)
        || type.SequenceEqual("tIME"u8);

    // ---------------------------------------------------------------- WebP

    private const int RiffHeaderLength = 12;

    /// <summary>VP8X: bits de "tem EXIF" e "tem XMP" no primeiro byte das flags.</summary>
    private const byte Vp8xExifAndXmpFlags = 0x08 | 0x04;

    /// <summary>Sai: os chunks <c>EXIF</c> e <c>XMP </c>; as flags do <c>VP8X</c> são ajustadas.</summary>
    private static MemoryStream? StripWebp(ReadOnlySpan<byte> src)
    {
        if (src.Length < RiffHeaderLength)
            return null;

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        if (riffSize < 4 || riffSize > (uint)(src.Length - 8))
            return null;

        var end = 8 + (int)riffSize;

        var output = new MemoryStream(src.Length);
        output.Write(src[..RiffHeaderLength]);

        var pos = RiffHeaderLength;
        while (pos < end)
        {
            if (pos + 8 > end)
                return null;

            var fourCc = src.Slice(pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(src[(pos + 4)..]);
            if (size > (uint)(end - pos - 8))
                return null;

            // Chunk de tamanho ímpar leva um byte de preenchimento.
            var chunkLength = Math.Min(8 + (int)size + (int)(size & 1), end - pos);

            if (!fourCc.SequenceEqual("EXIF"u8) && !fourCc.SequenceEqual("XMP "u8))
            {
                var chunkStart = (int)output.Position;
                output.Write(src.Slice(pos, chunkLength));

                if (fourCc.SequenceEqual("VP8X"u8) && size >= 1)
                    output.GetBuffer()[chunkStart + 8] &= unchecked((byte)~Vp8xExifAndXmpFlags);
            }

            pos += chunkLength;
        }

        // Novo tamanho do RIFF: tudo depois dos 8 primeiros bytes.
        BinaryPrimitives.WriteUInt32LittleEndian(output.GetBuffer().AsSpan(4), (uint)(output.Length - 8));

        return output;
    }
}
