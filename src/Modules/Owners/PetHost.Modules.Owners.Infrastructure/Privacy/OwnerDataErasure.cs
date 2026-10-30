using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;

namespace PetHost.Modules.Owners.Infrastructure.Privacy;

/// <summary>Adaptador da porta <see cref="IOwnerDataErasure"/> sobre o contrato <see cref="IPersonalDataErasure"/>.</summary>
internal sealed class OwnerDataErasure(IPersonalDataErasure personalDataErasure) : IOwnerDataErasure
{
    public Task EraseAsync(Guid userId, Guid ownerId, CancellationToken cancellationToken) =>
        personalDataErasure.EraseAsync(new ErasedAccount(userId, Roles.Owner, ownerId), cancellationToken);
}
