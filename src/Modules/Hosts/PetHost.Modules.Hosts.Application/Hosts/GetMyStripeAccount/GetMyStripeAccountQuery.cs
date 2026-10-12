using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.GetMyStripeAccount;

/// <summary>"Meus recebimentos": a situação do cadastro no Stripe, como está gravada (sem chamar o Stripe).</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record GetMyStripeAccountQuery(Guid UserId) : IQuery<StripeAccountResponse>;

public sealed class GetMyStripeAccountQueryHandler(IHostRepository hostRepository)
    : IQueryHandler<GetMyStripeAccountQuery, StripeAccountResponse>
{
    public async Task<Result<StripeAccountResponse>> HandleAsync(GetMyStripeAccountQuery query, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        return host is null
            ? Result<StripeAccountResponse>.Failure(HostsErrors.ProfileNotFound)
            : Result<StripeAccountResponse>.Success(StripeAccountResponse.From(host));
    }
}
