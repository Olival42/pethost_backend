using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.GetMyHost;

/// <summary>O perfil de anfitrião (verificado) da conta logada.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record GetMyHostQuery(Guid UserId) : IQuery<HostResponse>;

public sealed class GetMyHostQueryHandler(IHostRepository hostRepository, IHostAccounts accounts)
    : IQueryHandler<GetMyHostQuery, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(GetMyHostQuery query, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.ProfileNotFound);

        var account = await HostApplicationSupport.GetAccountAsync(accounts, host.UserId, cancellationToken).ConfigureAwait(false);

        return Result<HostResponse>.Success(HostResponse.From(host, account));
    }
}
