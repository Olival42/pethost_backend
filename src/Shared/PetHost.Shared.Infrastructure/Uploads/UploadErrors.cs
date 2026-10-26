using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>Erros do upload direto ao bucket (<c>/api/v1/uploads</c>).</summary>
public static class UploadErrors
{
    public static readonly Error PurposeRequired =
        Error.Validation("purpose", "UPLOAD_PURPOSE_REQUIRED", "Purpose is required.");

    public static readonly Error PurposeInvalid =
        Error.Validation("purpose", "UPLOAD_PURPOSE_INVALID", "Purpose is not a known upload purpose.");

    /// <summary>A finalidade existe, mas não é do papel do token (tutor pedindo foto de cantinho).</summary>
    public static readonly Error PurposeForbidden =
        new("UPLOAD_PURPOSE_FORBIDDEN", "Your account cannot upload files for this purpose.");

    public static readonly Error ContentTypeRequired =
        Error.Validation("contentType", "UPLOAD_CONTENT_TYPE_REQUIRED", "Content type is required.");

    public static readonly Error ContentTypeInvalid =
        Error.Validation("contentType", "UPLOAD_CONTENT_TYPE_INVALID", "Content type must be image/jpeg, image/png or image/webp.");

    public static readonly Error SizeRequired =
        Error.Validation("sizeBytes", "UPLOAD_SIZE_REQUIRED", "File size is required.");

    public static readonly Error SizeInvalid =
        Error.Validation("sizeBytes", "UPLOAD_SIZE_INVALID", "File size must be greater than zero.");

    public static readonly Error SizeTooLarge =
        Error.Validation("sizeBytes", "UPLOAD_SIZE_TOO_LARGE", $"The image must be at most {ImageRules.MaxBytes / (1024 * 1024)} MB.", ("maxBytes", ImageRules.MaxBytes));

    public static Error ResourceIdRequired(string resource) =>
        Error.Validation("resourceId", "UPLOAD_RESOURCE_ID_REQUIRED", $"Resource id ({resource}) is required for this purpose.", ("resource", resource));

    public static readonly Error ResourceIdNotAllowed =
        Error.Validation("resourceId", "UPLOAD_RESOURCE_ID_NOT_ALLOWED", "This purpose does not take a resource id.");

    public static readonly Error PhotoIdNotAllowed =
        Error.Validation("photoId", "UPLOAD_PHOTO_ID_NOT_ALLOWED", "This purpose does not take a photo id.");

    public static readonly Error KindRequired =
        Error.Validation("kind", "UPLOAD_KIND_REQUIRED", "Kind is required for this purpose.");

    public static readonly Error KindInvalid =
        Error.Validation("kind", "UPLOAD_KIND_INVALID", "Kind is not valid for this purpose.");

    public static readonly Error KindNotAllowed =
        Error.Validation("kind", "UPLOAD_KIND_NOT_ALLOWED", "This purpose does not take a kind.");

    public static Error PendingLimitReached(int max) =>
        new Error("UPLOAD_PENDING_LIMIT_REACHED", $"You have {max} uploads waiting for confirmation. Confirm them or wait for them to expire.")
            .WithParams(("max", max));

    /// <summary>Não existe, é de outra conta ou de outro papel: a mesma resposta, para não revelar nada.</summary>
    public static readonly Error NotFound =
        new("UPLOAD_NOT_FOUND", "Upload not found.");

    public static readonly Error Expired =
        new("UPLOAD_EXPIRED", "This upload expired. Request a new upload URL.");

    /// <summary>Já foi confirmado uma vez e recusado (arquivo inválido ou regra de negócio): o arquivo foi apagado.</summary>
    public static readonly Error AlreadyRejected =
        new("UPLOAD_ALREADY_REJECTED", "This upload was rejected. Request a new upload URL.");

    /// <summary>Outra confirmação do mesmo upload está em andamento.</summary>
    public static readonly Error ConfirmationConflict =
        new("UPLOAD_CONFIRMATION_CONFLICT", "This upload is already being confirmed. Try again in a few seconds.");

    /// <summary>O arquivo ainda não chegou ao bucket. Dá para enviar e confirmar de novo enquanto a URL vale.</summary>
    public static readonly Error FileNotReceived =
        new("UPLOAD_FILE_NOT_RECEIVED", "The file was not uploaded yet. Send it to the upload URL before confirming.");
}
