using System.Globalization;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.GetHostById;

/// <summary>Um anfitrião com os dados da conta. Uso do admin.</summary>
public sealed record GetHostByIdQuery(Guid HostId) : IQuery<HostResponse>;

/// <summary>Todos os anfitriões com os dados da conta. Uso do admin; sem paginação nem filtro no MVP.</summary>
public sealed record ListHostsQuery : IQuery<IReadOnlyList<HostResponse>>;

public sealed class GetHostByIdQueryHandler(
    IHostRepository hostRepository,
    IHostAccounts accounts,
    IAuditTrail auditTrail) : IQueryHandler<GetHostByIdQuery, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(GetHostByIdQuery query, CancellationToken cancellationToken)
    {
        var id = new HostId(query.HostId);
        var host = await hostRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.NotFound(id));

        // O admin está vendo CPF completo e os dados da conta de outra pessoa (LGPD).
        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostViewed, AuditTargets.Host, host.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostResponse>.Success(await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class ListHostsQueryHandler(
    IHostRepository hostRepository,
    IHostAccounts accounts,
    IAuditTrail auditTrail) : IQueryHandler<ListHostsQuery, IReadOnlyList<HostResponse>>
{
    public async Task<Result<IReadOnlyList<HostResponse>>> HandleAsync(ListHostsQuery query, CancellationToken cancellationToken)
    {
        var hosts = await hostRepository.ListAsync(cancellationToken).ConfigureAwait(false);

        // Uma consulta ao Auth para a lista inteira.
        var found = await accounts.GetByIdsAsync([.. hosts.Select(h => h.UserId)], cancellationToken).ConfigureAwait(false);
        var byId = found.ToDictionary(a => a.Id);

        IReadOnlyList<HostResponse> response = [.. hosts.Select(h => HostResponse.From(h, byId.GetValueOrDefault(h.UserId)))];

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostsListed,
                    AuditTargets.Host,
                    TargetId: null,
                    Details: new Dictionary<string, string?> { ["count"] = response.Count.ToString(CultureInfo.InvariantCulture) }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<HostResponse>>.Success(response);
    }
}
