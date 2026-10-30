using System.Globalization;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.ListHostApplications;

/// <summary>Solicitações num status — por padrão a fila (<c>submitted</c>). Uso do admin.</summary>
/// <param name="Status">Filtro opcional (<c>?status=</c>). Vazio = <c>submitted</c>.</param>
public sealed record ListHostApplicationsQuery(string? Status) : IQuery<IReadOnlyList<HostApplicationReviewResponse>>;

/// <summary>
/// A fila do admin, da mais antiga para a mais nova, com os dados da conta. Sem paginação:
/// lista administrativa (§13). Fica na trilha, porque mostra CPF completo.
/// </summary>
public sealed class ListHostApplicationsQueryHandler(
    IHostApplicationRepository applicationRepository,
    IHostAccounts accounts,
    IAuditTrail auditTrail) : IQueryHandler<ListHostApplicationsQuery, IReadOnlyList<HostApplicationReviewResponse>>
{
    public async Task<Result<IReadOnlyList<HostApplicationReviewResponse>>> HandleAsync(
        ListHostApplicationsQuery query,
        CancellationToken cancellationToken)
    {
        var status = HostApplicationStatus.Submitted;
        if (!string.IsNullOrWhiteSpace(query.Status) && !HostApplicationStatusValues.TryParse(query.Status, out status))
            return Result<IReadOnlyList<HostApplicationReviewResponse>>.Failure(HostsErrors.StatusFilterInvalid);

        var applications = await applicationRepository.ListByStatusAsync(status, cancellationToken).ConfigureAwait(false);

        // Uma consulta ao Auth para a lista inteira, não uma por solicitação.
        var found = await accounts
            .GetByIdsAsync([.. applications.Select(a => a.UserId).Distinct()], cancellationToken)
            .ConfigureAwait(false);
        var accountsById = found.ToDictionary(a => a.Id);

        IReadOnlyList<HostApplicationReviewResponse> response =
        [
            .. applications.Select(a => HostApplicationReviewResponse.From(a, accountsById.GetValueOrDefault(a.UserId))),
        ];

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostApplicationsListed,
                    AuditTargets.HostApplication,
                    TargetId: null,
                    Details: new Dictionary<string, string?>
                    {
                        ["status"] = HostApplicationStatusValues.ToWire(status),
                        ["count"] = response.Count.ToString(CultureInfo.InvariantCulture),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<HostApplicationReviewResponse>>.Success(response);
    }
}
