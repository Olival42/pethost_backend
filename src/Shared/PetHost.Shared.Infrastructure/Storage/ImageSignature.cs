namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Descobre o formato da imagem pelos primeiros bytes ("assinatura"), não pelo nome nem pelo
/// content-type declarado — os dois o cliente escolhe. Um <c>.exe</c> renomeado para
/// <c>.png</c> não passa.
/// </summary>
public static class ImageSignature
{
    /// <summary>Bytes necessários para reconhecer qualquer formato aceito.</summary>
    public const int HeaderLength = 12;

    /// <summary>O formato da imagem, ou <c>null</c> se não for JPEG, PNG nem WebP.</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ImageFormat.Jpeg;

        // PNG: 89 'P' 'N' 'G' 0D 0A 1A 0A
        if (header.Length >= 8 && header[..8].SequenceEqual(Png))
            return ImageFormat.Png;

        // WebP: 'RIFF' <tamanho> 'WEBP'
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return ImageFormat.Webp;

        return null;
    }

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
}

/// <summary>Formato aceito, com o content-type e a extensão gravados no bucket.</summary>
public sealed record ImageFormat(string ContentType, string Extension)
{
    public static readonly ImageFormat Jpeg = new("image/jpeg", ".jpg");
    public static readonly ImageFormat Png = new("image/png", ".png");
    public static readonly ImageFormat Webp = new("image/webp", ".webp");
}
