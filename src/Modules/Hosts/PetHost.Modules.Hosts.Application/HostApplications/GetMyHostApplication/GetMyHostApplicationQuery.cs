using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.GetMyHostApplication;

/// <summary>A tela "Minha verificação" da conta logada.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record GetMyHostApplicationQuery(Guid UserId) : IQuery<MyHostApplicationResponse>;

/// <summary>
/// A solicitação mais recente e se dá para abrir outra. Nunca 404: quem nunca pediu recebe
/// <c>application: null</c> e <c>canStartNew: true</c>.
/// </summary>
public sealed class GetMyHostApplicationQueryHandler(IHostApplicationRepository applicationRepository)
    : IQueryHandler<GetMyHostApplicationQuery, MyHostApplicationResponse>
{
    public async Task<Result<MyHostApplicationResponse>> HandleAsync(GetMyHostApplicationQuery query, CancellationToken cancellationToken)
    {
        var history = await applicationRepository.ListByUserIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        var latest = history.Count > 0 ? history[0] : null;

        return Result<MyHostApplicationResponse>.Success(new MyHostApplicationResponse(
            latest is null ? null : HostApplicationResponse.From(latest),
            HostApplication.CanStart(history),
            HostApplication.RemainingAttempts(history),
            HostVerificationNotice.CurrentVersion));
    }
}
