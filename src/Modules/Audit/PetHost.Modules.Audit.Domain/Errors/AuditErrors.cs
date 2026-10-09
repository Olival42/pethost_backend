using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Audit.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Audit. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class AuditErrors
{
    public const int MaxPageSize = 100;

    public static readonly Error PageInvalid =
        Error.Validation("page", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", $"Page size must be between 1 and {MaxPageSize}.");

    public static readonly Error PeriodInvalid =
        Error.Validation("from", "'from' must be before 'to'.");
}
