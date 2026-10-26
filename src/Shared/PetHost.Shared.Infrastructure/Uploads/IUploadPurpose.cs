using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Uma finalidade de upload direto ao bucket (<c>pet-photo</c>, <c>owner-avatar</c>...). Cada
/// módulo registra as suas (<c>AddScoped&lt;IUploadPurpose, ...&gt;</c>), como os handlers de
/// webhook do Stripe; <c>/api/v1/uploads</c> é um só para a API inteira.
/// </summary>
/// <remarks>
/// A finalidade não reimplementa regra nenhuma: chama o <b>mesmo caso de uso</b> do upload por
/// multipart. No pedido, com <see cref="ImageUpload.Preflight"/> (confere posse e regras sem
/// gravar); na confirmação, com os bytes que o navegador mandou ao bucket — que o caso de uso
/// confere (formato pelo conteúdo, tamanho, metadados removidos) como sempre.
/// </remarks>
public interface IUploadPurpose
{
    /// <summary>Nome no contrato (<c>purpose</c>), em kebab-case.</summary>
    string Name { get; }

    /// <summary>Papéis do token que podem usar (<c>owner</c>, <c>host</c>).</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Que campos de alvo o pedido leva.</summary>
    UploadTargetRules Target { get; }

    /// <summary>
    /// Confere, sem gravar nada, se quem pede pode enviar este arquivo agora (posse, limite de
    /// fotos, conversa bloqueada...). Use <see cref="UploadResults.FromPreflight{T}"/>.
    /// </summary>
    Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica o arquivo recebido ao recurso, pelo caso de uso de sempre. A resposta é a do caso de
    /// uso (o pet, o tutor, a mensagem): use <see cref="UploadResults.FromCompletion{T}"/>.
    /// </summary>
    Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken);
}

/// <summary>Campos de alvo de uma finalidade.</summary>
/// <param name="Resource">Nome do recurso em <c>resourceId</c> (<c>petId</c>, <c>listingId</c>...), ou <c>null</c> se não leva.</param>
/// <param name="AllowsPhotoId">Aceita <c>photoId</c> (substituir uma foto em vez de adicionar).</param>
/// <param name="Kinds">Valores aceitos em <c>kind</c> (obrigatório quando há), ou <c>null</c> se não leva.</param>
public sealed record UploadTargetRules(string? Resource = null, bool AllowsPhotoId = false, IReadOnlyList<string>? Kinds = null)
{
    public static UploadTargetRules None { get; } = new();
}

/// <summary>O upload como a finalidade vê.</summary>
/// <param name="UploadId">Id do upload: também serve de id do que for criado, para a confirmação repetida não duplicar.</param>
/// <param name="UserId">Quem pediu (do token).</param>
/// <param name="Role">Papel do token no pedido.</param>
/// <param name="Body">Texto que vem na confirmação (legenda da foto do chat). Nulo no pedido.</param>
public sealed record UploadPurposeContext(
    Guid UploadId,
    Guid UserId,
    string Role,
    Guid? ResourceId,
    Guid? PhotoId,
    string? Kind,
    string? Body);

/// <summary>Converte o resultado do caso de uso no que <see cref="IUploadPurpose"/> devolve.</summary>
public static class UploadResults
{
    /// <summary>
    /// Resultado do caso de uso rodado com <see cref="ImageUpload.Preflight"/>: chegar ao bucket
    /// (<see cref="ImageErrors.PreflightPassed"/>) é sucesso; qualquer outro erro é a recusa.
    /// </summary>
    public static Result<Unit> FromPreflight<T>(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess || result.Errors!.All(error => error.Code == ImageErrors.PreflightPassed.Code))
            return Result<Unit>.Success(Unit.Value);

        return Result<Unit>.Failure(result.Errors!.Where(error => error.Code != ImageErrors.PreflightPassed.Code));
    }

    public static Result<object> FromCompletion<T>(Result<T> result)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsFailure ? Result<object>.FromFailure(result) : Result<object>.Success(result.Value!);
    }
}
