using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>
/// Marca uma action de upload de imagem: aceita <c>multipart/form-data</c> com a imagem na
/// parte <c>file</c> (ou nas partes de <see cref="Fields"/>) e limita o corpo a
/// <see cref="ImageRules.MaxRequestBytes"/> por imagem (<see cref="MaxImages"/>). O mesmo em
/// todo endpoint de foto, de qualquer módulo.
/// </summary>
/// <remarks>
/// O form é lido aqui, antes do model binding: corpo acima do teto vira <c>413
/// PAYLOAD_TOO_LARGE</c> no envelope, em vez de um binding pela metade (com o id da rota
/// vazio) chegar ao caso de uso.
/// <para>
/// Não herda de <see cref="ConsumesAttribute"/> de propósito: ele vira restrição de rota, e
/// content-type errado cairia num 415 do roteamento, sem dizer o formato certo. Aqui a rota
/// casa e o filtro responde o 415 com a mensagem do upload.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpPost("{petId:guid}/photos")]
/// [ImageUploadEndpoint]
/// public async Task&lt;IActionResult&gt; AddPhotoAsync(Guid petId, IFormFile? file, CancellationToken ct)
///     => ... new AddPetPhotoCommand(..., file.ToImageUpload()) ...
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ImageUploadEndpointAttribute : Attribute, IAsyncResourceFilter, IApiRequestMetadataProvider
{
    private const string MultipartFormData = "multipart/form-data";

    /// <summary>
    /// Quantas imagens o pedido leva (padrão 1). O teto do corpo é
    /// <see cref="ImageRules.MaxRequestBytes"/> por imagem.
    /// </summary>
    public int MaxImages { get; init; } = 1;

    /// <summary>As partes do multipart que levam imagem, para a mensagem de formato errado.</summary>
    public string Fields { get; init; } = ImageErrors.Field;

    private long MaxRequestBytes => ImageRules.MaxRequestBytes * Math.Max(1, MaxImages);

    /// <summary>Documenta no OpenAPI que o corpo é multipart (o Swagger mostra o seletor de arquivo).</summary>
    public void SetContentTypes(MediaTypeCollection contentTypes)
    {
        ArgumentNullException.ThrowIfNull(contentTypes);

        contentTypes.Clear();
        contentTypes.Add(MultipartFormData);
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;

        // Content-type errado (JSON, imagem crua): 415 dizendo o formato certo, em vez do
        // "Send application/json" genérico das outras rotas.
        if (request.ContentType is null
            || !request.ContentType.StartsWith(MultipartFormData, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = WrongContentType();
            return;
        }

        if (request.ContentLength > MaxRequestBytes)
        {
            context.Result = TooLarge();
            return;
        }

        // Sem Content-Length (envio em partes), o teto vale durante a leitura.
        if (context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } maxBody)
            maxBody.MaxRequestBodySize = MaxRequestBytes;

        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(
            request,
            new FormOptions { MultipartBodyLengthLimit = MaxRequestBytes }));

        try
        {
            await request.ReadFormAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Result = TooLarge();
            return;
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = TooLarge();
            return;
        }
        catch (InvalidDataException)
        {
            // Multipart ilegível: para quem envia, é o mesmo que não mandar o arquivo.
            context.Result = Unreadable();
            return;
        }

        await next().ConfigureAwait(false);
    }

    private ObjectResult WrongContentType() =>
        new(ApiResponse<Unit>.Fail(new ErrorResponse(
            ErrorCodes.UnsupportedMediaType,
            Fields == ImageErrors.Field
                ? $"Unsupported content type. Send multipart/form-data, with the image in the '{ImageErrors.Field}' field."
                : $"Unsupported content type. Send multipart/form-data, with the images in the fields: {Fields}.")))
        {
            StatusCode = StatusCodes.Status415UnsupportedMediaType,
        };

    private static ObjectResult Unreadable() =>
        new(ApiResponse<Unit>.Fail(new ErrorResponse(
            ErrorCodes.Validation,
            "Validation failed",
            new List<DataErrors> { new(ImageErrors.Field, [ImageErrors.FileRequired.Message], [FieldErrorItem.From(ImageErrors.FileRequired)]) })))
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };

    private ObjectResult TooLarge() =>
        new(ApiResponse<Unit>.Fail(new ErrorResponse(
            ErrorCodes.PayloadTooLarge,
            $"The request body must be at most {MaxRequestBytes / (1024 * 1024)} MB. Images up to {ImageRules.MaxBytes / (1024 * 1024)} MB are accepted.")))
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
        };
}

/// <summary>Converte o arquivo do multipart no contrato de upload.</summary>
public static class FormFileExtensions
{
    /// <summary>
    /// <c>null</c> quando a parte <c>file</c> não veio: o caso de uso responde
    /// <see cref="ImageErrors.FileRequired"/>.
    /// </summary>
    public static ImageUpload? ToImageUpload(this IFormFile? file) =>
        file is null ? null : new ImageUpload(file.OpenReadStream(), file.FileName, file.ContentType);
}
