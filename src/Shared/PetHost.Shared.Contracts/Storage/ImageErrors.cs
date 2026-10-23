using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Contracts.Storage;

/// <summary>
/// Erros do arquivo de imagem, iguais em todos os módulos. Sempre no campo <c>file</c>, o
/// nome da parte do multipart.
/// </summary>
public static class ImageErrors
{
    public const string Field = "file";

    public static readonly Error FileRequired =
        Error.Validation(Field, "IMAGE_FILE_REQUIRED", "Send the image in the 'file' field of a multipart/form-data request.");

    public static readonly Error FileEmpty =
        Error.Validation(Field, "IMAGE_FILE_EMPTY", "The image file is empty.");

    public static readonly Error FileTooLarge =
        Error.Validation(Field, "IMAGE_FILE_TOO_LARGE", $"The image must be at most {ImageRules.MaxBytes / (1024 * 1024)} MB.", ("maxMegabytes", ImageRules.MaxBytes / (1024 * 1024)));

    public static readonly Error UnsupportedFormat =
        Error.Validation(Field, "IMAGE_UNSUPPORTED_FORMAT", "The image must be a JPG/JPEG, PNG or WebP file.");

    /// <summary>O formato bate, mas a estrutura do arquivo está quebrada (cortado, blocos inválidos).</summary>
    public static readonly Error FileCorrupted =
        Error.Validation(Field, "IMAGE_FILE_CORRUPTED", "The image file is corrupted and could not be read.");
}
