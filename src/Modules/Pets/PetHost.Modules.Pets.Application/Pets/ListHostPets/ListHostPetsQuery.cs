using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ListHostPets;

/// <summary>
/// Os pets que moram na casa de um anfitrião: o tutor vê com quem o pet dele vai conviver
/// durante a estadia. Só os ativos.
/// </summary>
/// <param name="HostId">Id do anfitrião (não o da conta).</param>
public sealed record ListHostPetsQuery(Guid HostId) : IQuery<KeeperPetsResponse>;

/// <summary>Sem paginação nem filtro: pedido do produto, e uma casa tem poucos pets.</summary>
public sealed class ListHostPetsQueryHandler(
    IPetRepository petRepository,
    IPetKeepers petKeepers) : IQueryHandler<ListHostPetsQuery, KeeperPetsResponse>
{
    public async Task<Result<KeeperPetsResponse>> HandleAsync(ListHostPetsQuery query, CancellationToken cancellationToken)
    {
        if (query.HostId == Guid.Empty
            || !await petKeepers.HostExistsAsync(query.HostId, cancellationToken).ConfigureAwait(false))
        {
            return Result<KeeperPetsResponse>.Failure(PetsErrors.HostNotFound(query.HostId));
        }

        var host = PetKeeper.Host(query.HostId);
        var pets = await petRepository
            .ListActiveByKeeperAsync(host, cancellationToken)
            .ConfigureAwait(false);

        return Result<KeeperPetsResponse>.Success(
            await KeeperPetsResponse.LoadAsync(host, pets, petKeepers, cancellationToken).ConfigureAwait(false));
    }
}
