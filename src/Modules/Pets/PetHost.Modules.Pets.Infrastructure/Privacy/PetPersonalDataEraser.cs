using PetHost.Modules.Pets.Application.Pets.ErasePetsOfKeeper;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Infrastructure.Privacy;

/// <summary>
/// A parte do Pets na exclusão da conta (§5, <see cref="IPersonalDataEraser"/>): os pets do tutor
/// ou do anfitrião ficam inativos e sem fotos. Anfitrião ainda sem perfil não tem pet.
/// </summary>
internal sealed class PetPersonalDataEraser(
    ICommandHandler<ErasePetsOfKeeperCommand, int> erasePets) : IPersonalDataEraser
{
    public async Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.ProfileId is not { } keeperId)
            return;

        var keeperType = account.Role == Roles.Owner ? KeeperType.Owner : KeeperType.Host;

        await erasePets.HandleAsync(new ErasePetsOfKeeperCommand(keeperType, keeperId), cancellationToken).ConfigureAwait(false);
    }
}
