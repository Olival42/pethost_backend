namespace PetHost.Shared.Kernel.Errors;

/// <summary>Códigos de erro transversais. Contrato público: nunca mudam de valor.</summary>
public static class ErrorCodes
{
    public const string Validation = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string BusinessRule = "BUSINESS_RULE_VIOLATION";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string Unexpected = "UNEXPECTED_ERROR";
}
