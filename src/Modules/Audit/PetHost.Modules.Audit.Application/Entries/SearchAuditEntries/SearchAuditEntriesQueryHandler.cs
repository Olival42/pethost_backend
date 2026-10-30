using System.Globalization;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Modules.Audit.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;

/// <summary>
/// Busca na trilha, do mais novo para o mais antigo. A própria consulta também é
/// registrada: quem lê a trilha vê IP e ações de outras pessoas.
/// </summary>
public sealed class SearchAuditEntriesQueryHandler(
    IAuditEntryRepository repository,
    IAuditTrail auditTrail) : IQueryHandler<SearchAuditEntriesQuery, PagedResult<AuditEntryResponse>>
{
    public async Task<Result<PagedResult<AuditEntryResponse>>> HandleAsync(
        SearchAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();
        if (query.Page < 1)
            errors.Add(AuditErrors.PageInvalid);
        if (query.PageSize is < 1 or > AuditErrors.MaxPageSize)
            errors.Add(AuditErrors.PageSizeInvalid);
        if (query.From is { } from && query.To is { } to && from > to)
            errors.Add(AuditErrors.PeriodInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<AuditEntryResponse>>.Failure(errors);

        var filter = new AuditEntryFilter(query.Action, query.TargetType, query.TargetId, query.ActorId, query.From, query.To);
        var (items, total) = await repository
            .SearchAsync(filter, query.Page, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.AuditSearched,
                    AuditTargets.Audit,
                    TargetId: null,
                    Details: new Dictionary<string, string?>
                    {
                        ["action"] = query.Action,
                        ["targetType"] = query.TargetType,
                        ["targetId"] = query.TargetId?.ToString(),
                        ["actorId"] = query.ActorId?.ToString(),
                        ["page"] = query.Page.ToString(CultureInfo.InvariantCulture),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PagedResult<AuditEntryResponse>>.Success(new PagedResult<AuditEntryResponse>(
            [.. items.Select(AuditEntryResponse.From)],
            query.Page,
            query.PageSize,
            total));
    }
}
