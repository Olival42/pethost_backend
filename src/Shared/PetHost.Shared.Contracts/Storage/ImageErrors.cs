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
        Error.Validation(Field, "Send the image in the 'file' field of a multipart/form-data request.");

    public static readonly Error FileEmpty =
        Error.Validation(Field, "The image file is empty.");

    public static readonly Error FileTooLarge =
        Error.Validation(Field, $"The image must be at most {ImageRules.MaxBytes / (1024 * 1024)} MB.");

    public static readonly Error UnsupportedFormat =
        Error.Validation(Field, "The image must be a JPG/JPEG, PNG or WebP file.");
}
