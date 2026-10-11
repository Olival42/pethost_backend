using System.Security.Cryptography;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Lê e confere a imagem enviada, igual para o bucket público e para o privado: tamanho,
/// arquivo vazio e formato pelos primeiros bytes (<see cref="ImageSignature"/>). Depois tira os
/// metadados (<see cref="ImageMetadataStripper"/>): é essa versão que vai para o bucket.
/// </summary>
internal static class ImageUploadReader
{
    /// <summary>A imagem inteira na memória, já conferida e sem metadados, ou o erro do campo <c>file</c>.</summary>
    public static async Task<Result<ReadImage>> ReadAsync(ImageUpload? image, CancellationToken cancellationToken)
    {
        if (image is null)
            return Result<ReadImage>.Failure(ImageErrors.FileRequired);

        // Lê no máximo um byte além do limite: o bastante para saber que passou, sem
        // carregar um arquivo gigante inteiro na memória.
        var buffer = new MemoryStream();
        var tooLarge = await CopyUpToAsync(image.Content, buffer, ImageRules.MaxBytes, cancellationToken).ConfigureAwait(false);

        try
        {
            if (tooLarge)
                return Result<ReadImage>.Failure(ImageErrors.FileTooLarge);
            if (buffer.Length == 0)
                return Result<ReadImage>.Failure(ImageErrors.FileEmpty);

            return Clean(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        finally
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Confere o formato e devolve uma cópia sem metadados: GPS e afins não chegam ao bucket.</summary>
    private static Result<ReadImage> Clean(ReadOnlySpan<byte> bytes)
    {
        var format = ImageSignature.Detect(bytes[..Math.Min(bytes.Length, ImageSignature.HeaderLength)]);
        if (format is null)
            return Result<ReadImage>.Failure(ImageErrors.UnsupportedFormat);

        var stripped = ImageMetadataStripper.Strip(bytes, format);
        if (stripped is null)
            return Result<ReadImage>.Failure(ImageErrors.FileCorrupted);

        stripped.Position = 0;
        return Result<ReadImage>.Success(new ReadImage(stripped, format));
    }

    /// <summary>Copia até <paramref name="limit"/> bytes. Devolve <c>true</c> se a origem tinha mais.</summary>
    private static async Task<bool> CopyUpToAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;

            total += read;
            if (total > limit)
                return true;

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Imagem lida e conferida. Quem recebe fecha o buffer.</summary>
internal sealed record ReadImage(MemoryStream Buffer, ImageFormat Format) : IDisposable
{
    public long Length => Buffer.Length;

    /// <summary>SHA-256 dos bytes, em hexadecimal minúsculo.</summary>
    public string ContentHash() =>
        Convert.ToHexStringLower(SHA256.HashData(Buffer.GetBuffer().AsSpan(0, (int)Buffer.Length)));

    public void Dispose() => Buffer.Dispose();
}
