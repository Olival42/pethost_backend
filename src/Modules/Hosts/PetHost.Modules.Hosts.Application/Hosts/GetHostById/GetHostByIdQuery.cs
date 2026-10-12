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

/// <summary>Todos os anfitriões com os dados da conta. Uso do admin; sem paginação no MVP.</summary>
/// <param name="StripeStatus">
/// Filtra pela situação do cadastro de recebimento (<c>not_started</c>, <c>in_progress</c>,
/// <c>under_review</c>, <c>restricted</c>, <c>rejected</c>, <c>enabled</c>): o suporte acha quem travou.
/// </param>
public sealed record ListHostsQuery(string? StripeStatus = null) : IQuery<IReadOnlyList<HostResponse>>;

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
        ArgumentNullException.ThrowIfNull(query);

        StripeOnboardingStatus? stripeStatus = null;
        if (!string.IsNullOrWhiteSpace(query.StripeStatus))
        {
            if (!StripeOnboardingStatusValues.TryFromWire(query.StripeStatus, out var parsed))
                return Result<IReadOnlyList<HostResponse>>.Failure(HostsErrors.StripeStatusFilterInvalid);

            stripeStatus = parsed;
        }

        var all = await hostRepository.ListAsync(cancellationToken).ConfigureAwait(false);

        // A situação é calculada do espelho do Stripe; a lista do admin é pequena, filtra na memória.
        var hosts = stripeStatus is null ? all : [.. all.Where(h => h.StripeOnboardingStatus == stripeStatus)];

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
                    Details: new Dictionary<string, string?>
                    {
                        ["count"] = response.Count.ToString(CultureInfo.InvariantCulture),
                        ["stripeStatus"] = stripeStatus is null ? null : StripeOnboardingStatusValues.ToWire(stripeStatus.Value),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<HostResponse>>.Success(response);
    }
}
