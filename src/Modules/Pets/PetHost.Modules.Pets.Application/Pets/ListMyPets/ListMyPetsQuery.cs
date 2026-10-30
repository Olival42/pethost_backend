using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ListMyPets;

/// <summary>Os pets da conta logada — do tutor ou do anfitrião —, ativos e inativos.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
public sealed record ListMyPetsQuery(Guid UserId, string Role) : IQuery<KeeperPetsResponse>;

/// <summary>Sem paginação nem filtro: pedido do produto, e uma pessoa tem poucos pets.</summary>
public sealed class ListMyPetsQueryHandler(
    IPetRepository petRepository,
    IPetKeepers petKeepers) : IQueryHandler<ListMyPetsQuery, KeeperPetsResponse>
{
    public async Task<Result<KeeperPetsResponse>> HandleAsync(ListMyPetsQuery query, CancellationToken cancellationToken)
    {
        var keeper = await petKeepers.FindByAccountAsync(query.UserId, query.Role, cancellationToken).ConfigureAwait(false);
        if (keeper is null)
            return Result<KeeperPetsResponse>.Failure(PetsErrors.KeeperNotFound);

        var pets = await petRepository.ListByKeeperAsync(keeper.Value, cancellationToken).ConfigureAwait(false);

        return Result<KeeperPetsResponse>.Success(
            await KeeperPetsResponse.LoadAsync(keeper.Value, pets, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
