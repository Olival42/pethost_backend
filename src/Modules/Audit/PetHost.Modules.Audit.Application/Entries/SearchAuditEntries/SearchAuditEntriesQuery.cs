using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;

/// <summary>Consulta paginada da trilha. Só admin. Todos os filtros são opcionais.</summary>
public sealed record SearchAuditEntriesQuery(
    int Page = 1,
    int PageSize = 20,
    string? Action = null,
    string? TargetType = null,
    Guid? TargetId = null,
    Guid? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IQuery<PagedResult<AuditEntryResponse>>;
