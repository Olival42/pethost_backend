using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Application.Pets.Responses;

/// <summary>
/// Os pets de um tutor ou anfitrião. O dono vem <b>uma vez</b>, no topo — todos os pets da
/// lista são dele —, e cada pet sai sem <c>ownerId</c>, <c>hostId</c> e <c>keeper</c>.
/// </summary>
/// <param name="Keeper">Nome e foto do dono. Nulo só se o perfil sumiu — não deveria acontecer.</param>
/// <param name="Pets">Do mais novo para o mais antigo. Lista vazia quando não há pets.</param>
public sealed record KeeperPetsResponse(KeeperSummary? Keeper, IReadOnlyList<PetResponse> Pets)
{
    /// <summary>Monta a lista buscando o dono uma vez só.</summary>
    public static async Task<KeeperPetsResponse> LoadAsync(
        PetKeeper keeper,
        IReadOnlyList<Pet> pets,
        IPetKeepers petKeepers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pets);
        ArgumentNullException.ThrowIfNull(petKeepers);

        var summaries = await petKeepers.GetSummariesAsync([keeper], cancellationToken).ConfigureAwait(false);

        return new KeeperPetsResponse(
            summaries.GetValueOrDefault(keeper),
            [.. pets.Select(PetResponse.InList)]);
    }
}
