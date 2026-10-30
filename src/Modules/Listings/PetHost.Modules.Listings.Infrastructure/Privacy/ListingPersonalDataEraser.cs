using PetHost.Modules.Listings.Application.Listings.EraseListingsOfHost;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Infrastructure.Privacy;

/// <summary>
/// A parte do Listings na exclusão da conta (§5, <see cref="IPersonalDataEraser"/>): só anfitrião
/// aprovado tem cantinho.
/// </summary>
internal sealed class ListingPersonalDataEraser(
    ICommandHandler<EraseListingsOfHostCommand, int> eraseListings) : IPersonalDataEraser
{
    public async Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Role != Roles.Host || account.ProfileId is not { } hostId)
            return;

        await eraseListings.HandleAsync(new EraseListingsOfHostCommand(hostId), cancellationToken).ConfigureAwait(false);
    }
}
