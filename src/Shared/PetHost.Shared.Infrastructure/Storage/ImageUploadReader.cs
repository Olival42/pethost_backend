using System.Security.Cryptography;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Lê e confere a imagem enviada, igual para o bucket público e para o privado: tamanho,
/// arquivo vazio e formato pelos primeiros bytes (<see cref="ImageSignature"/>).
/// </summary>
internal static class ImageUploadReader
{
    /// <summary>A imagem inteira na memória, já conferida, ou o erro do campo <c>file</c>.</summary>
    public static async Task<Result<ReadImage>> ReadAsync(ImageUpload? image, CancellationToken cancellationToken)
    {
        if (image is null)
            return Result<ReadImage>.Failure(ImageErrors.FileRequired);

        // Lê no máximo um byte além do limite: o bastante para saber que passou, sem
        // carregar um arquivo gigante inteiro na memória.
        var buffer = new MemoryStream();
        var tooLarge = await CopyUpToAsync(image.Content, buffer, ImageRules.MaxBytes, cancellationToken).ConfigureAwait(false);

        Error? error = null;
        ImageFormat? format = null;

        if (tooLarge)
            error = ImageErrors.FileTooLarge;
        else if (buffer.Length == 0)
            error = ImageErrors.FileEmpty;
        else
        {
            var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageSignature.HeaderLength));
            format = ImageSignature.Detect(header);
            if (format is null)
                error = ImageErrors.UnsupportedFormat;
        }

        if (error is not null)
        {
            await buffer.DisposeAsync().ConfigureAwait(false);
            return Result<ReadImage>.Failure(error);
        }

        buffer.Position = 0;
        return Result<ReadImage>.Success(new ReadImage(buffer, format!));
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
