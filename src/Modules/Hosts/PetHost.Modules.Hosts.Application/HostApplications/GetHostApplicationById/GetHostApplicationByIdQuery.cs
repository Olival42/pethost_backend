using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationById;

/// <summary>Uma solicitação com tudo o que o admin precisa para decidir.</summary>
public sealed record GetHostApplicationByIdQuery(Guid ApplicationId) : IQuery<HostApplicationReviewResponse>;

public sealed class GetHostApplicationByIdQueryHandler(
    IHostApplicationRepository applicationRepository,
    IHostAccounts accounts,
    IAuditTrail auditTrail) : IQueryHandler<GetHostApplicationByIdQuery, HostApplicationReviewResponse>
{
    public async Task<Result<HostApplicationReviewResponse>> HandleAsync(GetHostApplicationByIdQuery query, CancellationToken cancellationToken)
    {
        var id = new HostApplicationId(query.ApplicationId);
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return Result<HostApplicationReviewResponse>.Failure(HostsErrors.ApplicationNotFound(id));

        var account = await HostApplicationSupport.GetAccountAsync(accounts, application.UserId, cancellationToken).ConfigureAwait(false);

        // O admin está vendo CPF completo e os dados da conta de outra pessoa (LGPD).
        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostApplicationViewed, AuditTargets.HostApplication, application.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationReviewResponse>.Success(HostApplicationReviewResponse.From(application, account));
    }
}
