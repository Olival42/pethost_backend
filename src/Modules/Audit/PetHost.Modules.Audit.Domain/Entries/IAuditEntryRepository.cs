namespace PetHost.Modules.Audit.Domain.Entries;

/// <summary>
/// Consulta à trilha. A gravação é do adaptador do contrato (<c>AuditTrail</c>), num
/// contexto próprio; registro de auditoria não muda nem some.
/// </summary>
public interface IAuditEntryRepository
{
    /// <summary>Uma página dos registros que batem com o filtro, do mais novo para o mais antigo.</summary>
    Task<(IReadOnlyList<AuditEntry> Items, long TotalCount)> SearchAsync(
        AuditEntryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>Filtros da consulta. Nulo = não filtra por aquele campo.</summary>
public sealed record AuditEntryFilter(
    string? Action = null,
    string? TargetType = null,
    Guid? TargetId = null,
    Guid? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
