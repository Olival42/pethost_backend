using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Authorization;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

/// <summary>Contas e donos de exemplo, e o dublê da porta <see cref="IPetKeepers"/>.</summary>
internal static class PetsTestData
{
    public static readonly Guid OwnerUserId = Guid.CreateVersion7();
    public static readonly Guid OwnerId = Guid.CreateVersion7();
    public static readonly Guid HostUserId = Guid.CreateVersion7();
    public static readonly Guid HostId = Guid.CreateVersion7();
    public static readonly Guid StrangerUserId = Guid.CreateVersion7();
    public static readonly Guid StrangerOwnerId = Guid.CreateVersion7();

    public static PetKeeper Owner => PetKeeper.Owner(OwnerId);
    public static PetKeeper Host => PetKeeper.Host(HostId);

    /// <summary>
    /// Porta com três contas: a tutora (Camila), a anfitriã (Dona Cida) e outro tutor. Nomes
    /// e fotos para quem pedir.
    /// </summary>
    public static Mock<IPetKeepers> Keepers()
    {
        var keepers = new Mock<IPetKeepers>();

        keepers.Setup(k => k.FindByAccountAsync(OwnerUserId, Roles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(Owner);
        keepers.Setup(k => k.FindByAccountAsync(HostUserId, Roles.Host, It.IsAny<CancellationToken>())).ReturnsAsync(Host);
        keepers
            .Setup(k => k.FindByAccountAsync(StrangerUserId, Roles.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PetKeeper.Owner(StrangerOwnerId));

        keepers
            .Setup(k => k.GetSummariesAsync(It.IsAny<IReadOnlyCollection<PetKeeper>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<PetKeeper> requested, CancellationToken _) =>
                requested.ToDictionary(k => k, Summary));

        keepers.Setup(k => k.HostExistsAsync(HostId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        return keepers;
    }

    private static KeeperSummary Summary(PetKeeper keeper) =>
        keeper.Type == KeeperType.Owner
            ? new KeeperSummary(keeper.Id, "owner", "Camila Souza", null)
            : new KeeperSummary(keeper.Id, "host", "Dona Cida", "https://cdn.pethost.com/cida.png");
}
