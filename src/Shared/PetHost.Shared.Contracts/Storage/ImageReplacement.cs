using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Storage;

/// <summary>
/// O fluxo de trocar ou remover a foto de qualquer entidade, igual em todos os módulos: o
/// módulo só diz <b>como gravar a URL</b> na entidade dele.
/// </summary>
/// <remarks>
/// Bucket e banco não têm uma transação só, então a ordem evita sujeira dos dois lados:
/// <list type="number">
/// <item>envia a imagem nova (se for inválida, nada acontece);</item>
/// <item>grava a URL nova na entidade;</item>
/// <item>deu certo: apaga a imagem anterior. Falhou: apaga a nova (compensação).</item>
/// </list>
/// Confira antes se quem pede pode mexer na entidade — senão um estranho consegue enviar
/// arquivos para o bucket.
/// </remarks>
public static class ImageReplacement
{
    /// <summary>Troca a foto: envia <paramref name="image"/> e grava a URL com <paramref name="apply"/>.</summary>
    /// <param name="apply">
    /// Grava a imagem nova (URL e hash) na entidade e devolve a resposta e a URL que estava antes
    /// (para apagar). Se recusar (foto repetida, por exemplo), a imagem nova é apagada.
    /// </param>
    public static async Task<Result<T>> ReplaceAsync<T>(
        IImageStorage storage,
        ImageUpload? image,
        string folder,
        Func<StoredImage, CancellationToken, Task<Result<ImageChange<T>>>> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(apply);

        var stored = await storage.SaveAsync(image, folder, cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
            return Result<T>.FromFailure(stored);

        var url = stored.Value!.Url;

        Result<ImageChange<T>> applied;
        try
        {
            applied = await apply(stored.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // CancellationToken.None: a limpeza roda mesmo se o pedido caiu.
            await storage.DeleteAsync(url, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (applied.IsFailure)
        {
            await storage.DeleteAsync(url, CancellationToken.None).ConfigureAwait(false);
            return Result<T>.FromFailure(applied);
        }

        await storage.DeleteAsync(applied.Value!.PreviousUrl, CancellationToken.None).ConfigureAwait(false);

        return Result<T>.Success(applied.Value.Value);
    }

    /// <summary>Remove a foto: limpa a URL com <paramref name="apply"/> e apaga a imagem anterior.</summary>
    /// <param name="apply">Limpa a URL na entidade e devolve a resposta e a URL que estava antes.</param>
    public static async Task<Result<T>> RemoveAsync<T>(
        IImageStorage storage,
        Func<CancellationToken, Task<Result<ImageChange<T>>>> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(apply);

        var applied = await apply(cancellationToken).ConfigureAwait(false);
        if (applied.IsFailure)
            return Result<T>.FromFailure(applied);

        await storage.DeleteAsync(applied.Value!.PreviousUrl, CancellationToken.None).ConfigureAwait(false);

        return Result<T>.Success(applied.Value.Value);
    }
}

/// <summary>O que a entidade devolve depois de gravar a foto.</summary>
/// <param name="Value">A resposta do caso de uso.</param>
/// <param name="PreviousUrl">A foto que estava antes, para apagar do bucket. Nula se não havia.</param>
public sealed record ImageChange<T>(T Value, string? PreviousUrl);
