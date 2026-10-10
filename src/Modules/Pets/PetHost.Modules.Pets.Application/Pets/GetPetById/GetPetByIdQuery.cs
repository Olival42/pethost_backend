using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.GetPetById;

/// <summary>Um pet, com a ficha e o dono.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record GetPetByIdQuery(Guid UserId, string Role, Guid PetId) : IQuery<PetResponse>;

/// <summary>
/// Quem vê: o dono, sempre (inclusive inativo); qualquer conta logada, o pet <b>ativo</b> de
/// um anfitrião — é o que mostra ao tutor com quem o pet dele vai conviver; o admin, todos.
/// Pet de outro tutor não aparece: <c>404</c>, como se não existisse.
/// </summary>
public sealed class GetPetByIdQueryHandler(
    IPetRepository petRepository,
    IPetKeepers petKeepers) : IQueryHandler<GetPetByIdQuery, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(GetPetByIdQuery query, CancellationToken cancellationToken)
    {
        var id = new PetId(query.PetId);
        var pet = await petRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (pet is null || !await CanSeeAsync(pet, query, cancellationToken).ConfigureAwait(false))
            return Result<PetResponse>.Failure(PetsErrors.NotFound(id));

        return Result<PetResponse>.Success(
            await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false));
    }

    private async Task<bool> CanSeeAsync(Pet pet, GetPetByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.Role == Roles.Admin)
            return true;

        if (pet.Keeper.Type == KeeperType.Host && pet.IsActive)
            return true;

        var keeper = await petKeepers.FindByAccountAsync(query.UserId, query.Role, cancellationToken).ConfigureAwait(false);

        return keeper is not null && pet.IsKeptBy(keeper.Value);
    }
}
