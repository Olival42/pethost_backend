using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Pets.Application.Pets.ListHostPets;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Presentation.Pets;

/// <summary>
/// Os pets que moram na casa de um anfitrião. O anfitrião pode não ser uma empresa: com
/// isto o tutor sabe com quais animais o pet dele vai conviver durante a estadia.
/// </summary>
[ApiController]
[Route("api/v1/hosts/{hostId:guid}/pets")]
public sealed class HostPetsController(
    IQueryHandler<ListHostPetsQuery, KeeperPetsResponse> listHostPets) : ControllerBase
{
    /// <summary>Pets ativos do anfitrião, com a ficha. Qualquer conta logada. Sem paginação nem filtro.</summary>
    /// <remarks><c>hostId</c> é o id do anfitrião, não o da conta. Anfitrião inexistente: 404 <c>PET_HOST_NOT_FOUND</c>. O anfitrião vem uma vez, em <c>keeper</c>; os pets, em <c>pets</c>.</remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var result = await listHostPets.HandleAsync(new ListHostPetsQuery(hostId), cancellationToken);

        return result.ToActionResult();
    }
}
