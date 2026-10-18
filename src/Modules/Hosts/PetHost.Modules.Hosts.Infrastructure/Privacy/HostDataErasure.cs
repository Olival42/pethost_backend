using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;

namespace PetHost.Modules.Hosts.Infrastructure.Privacy;

/// <summary>Adaptador da porta <see cref="IHostDataErasure"/> sobre o contrato <see cref="IPersonalDataErasure"/>.</summary>
internal sealed class HostDataErasure(IPersonalDataErasure personalDataErasure) : IHostDataErasure
{
    public Task EraseAsync(Guid userId, Guid? hostId, CancellationToken cancellationToken) =>
        personalDataErasure.EraseAsync(new ErasedAccount(userId, Roles.Host, hostId), cancellationToken);
}
