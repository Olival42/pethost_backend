using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Pets.Application.Pets.AddPetPhoto;
using PetHost.Modules.Pets.Application.Pets.DeactivatePet;
using PetHost.Modules.Pets.Application.Pets.GetPetById;
using PetHost.Modules.Pets.Application.Pets.ListMyPets;
using PetHost.Modules.Pets.Application.Pets.ReactivatePet;
using PetHost.Modules.Pets.Application.Pets.RegisterPet;
using PetHost.Modules.Pets.Application.Pets.RemovePetPhoto;
using PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Application.Pets.UpdatePet;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Presentation.Pets;

/// <summary>
/// Pets: a ficha do animal. O tutor cadastra os pets que vai hospedar; o anfitrião, os que
/// moram na casa dele. O dono sai sempre do token — nunca do corpo.
/// </summary>
[ApiController]
[Route("api/v1/pets")]
public sealed class PetsController(
    ICommandHandler<RegisterPetCommand, PetResponse> registerPet,
    ICommandHandler<UpdatePetCommand, PetResponse> updatePet,
    ICommandHandler<DeactivatePetCommand, PetResponse> deactivatePet,
    ICommandHandler<ReactivatePetCommand, PetResponse> reactivatePet,
    ICommandHandler<AddPetPhotoCommand, PetResponse> addPetPhoto,
    ICommandHandler<ReplacePetPhotoCommand, PetResponse> replacePetPhoto,
    ICommandHandler<RemovePetPhotoCommand, PetResponse> removePetPhoto,
    IQueryHandler<GetPetByIdQuery, PetResponse> getPetById,
    IQueryHandler<ListMyPetsQuery, KeeperPetsResponse> listMyPets) : ControllerBase
{
    /// <summary>Papéis que têm pets: tutor e anfitrião.</summary>
    private const string Keepers = $"{Roles.Owner},{Roles.Host}";

    /// <summary>Cadastra um pet da conta logada (tutor ou anfitrião).</summary>
    /// <remarks>
    /// Obrigatórios: <c>species</c>, <c>name</c>, <c>sex</c>, <c>isNeutered</c>,
    /// <c>isVaccinated</c>, <c>goodWithDogs</c>, <c>goodWithCats</c>, <c>goodWithKids</c>;
    /// <c>size</c> obrigatório no cachorro, opcional no gato e proibido nos outros; <c>speciesDescription</c> só e sempre para
    /// <c>exotic</c>. Todos os erros voltam juntos num só 400. Conta sem perfil de tutor ou de
    /// anfitrião: 404 <c>PET_KEEPER_NOT_FOUND</c>. Microchip de outro pet ativo: 409
    /// <c>PET_MICROCHIP_ALREADY_REGISTERED</c>.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAsync([FromBody] PetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterPetCommand(
            User.GetUserId(),
            CurrentRole(),
            request.Species,
            request.SpeciesDescription,
            request.Name,
            request.Breed,
            request.Size,
            request.BirthDate,
            request.Sex,
            request.IsNeutered,
            request.IsVaccinated,
            request.MedicationNotes,
            request.FeedingNotes,
            request.GoodWithDogs,
            request.GoodWithCats,
            request.GoodWithKids,
            request.VetContact,
            request.Notes,
            request.WeightKg,
            request.Microchip,
            request.Allergies);

        var result = await registerPet.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult($"/api/v1/pets/{result.Value!.Id}")
            : result.ToActionResult();
    }

    /// <summary>Todos os pets da conta logada, ativos e inativos. Sem paginação nem filtro.</summary>
    /// <remarks>O dono vem uma vez, em <c>keeper</c>; os pets, em <c>pets</c>, sem os campos do dono.</remarks>
    [HttpGet("me")]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<KeeperPetsResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken)
    {
        var result = await listMyPets.HandleAsync(new ListMyPetsQuery(User.GetUserId(), CurrentRole()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Um pet, com a ficha e o dono.</summary>
    /// <remarks>
    /// O dono vê o pet sempre; qualquer conta logada vê o pet ativo de um anfitrião; o admin vê
    /// todos. Pet de outro tutor responde 404, como se não existisse.
    /// </remarks>
    [HttpGet("{petId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid petId, CancellationToken cancellationToken)
    {
        var result = await getPetById.HandleAsync(new GetPetByIdQuery(User.GetUserId(), CurrentRole(), petId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Altera a ficha de um pet da conta logada (PATCH: só o que vier muda).</summary>
    /// <remarks>
    /// Campo ausente ou <c>null</c> não mexe; <c>""</c> limpa um campo opcional. Trocar a
    /// espécie limpa o porte e a descrição que não vierem junto e que a nova espécie não tem
    /// (cachorro ↔ gato mantém o porte). A ficha resultante é
    /// validada inteira: todos os erros num só 400. Pet de outra conta responde 404
    /// <c>PET_NOT_FOUND</c>, igual a pet inexistente. Pet desativado também pode ser
    /// editado. Microchip novo que é de outro pet ativo: 409
    /// <c>PET_MICROCHIP_ALREADY_REGISTERED</c>.
    /// </remarks>
    [HttpPatch("{petId:guid}")]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid petId,
        [FromBody] PetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdatePetCommand(
            User.GetUserId(),
            CurrentRole(),
            petId,
            request.Species,
            request.SpeciesDescription,
            request.Name,
            request.Breed,
            request.Size,
            request.BirthDate,
            request.Sex,
            request.IsNeutered,
            request.IsVaccinated,
            request.MedicationNotes,
            request.FeedingNotes,
            request.GoodWithDogs,
            request.GoodWithCats,
            request.GoodWithKids,
            request.VetContact,
            request.Notes,
            request.WeightKg,
            request.Microchip,
            request.Allergies);

        var result = await updatePet.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Desativa um pet da conta logada. Idempotente.</summary>
    /// <remarks>
    /// Pet desativado some para quem não é o dono e pode voltar
    /// com <c>DELETE</c> nesta rota. Pet de outra conta: 404 <c>PET_NOT_FOUND</c>.
    /// </remarks>
    [HttpPost("{petId:guid}/deactivation")]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync(Guid petId, CancellationToken cancellationToken)
    {
        var result = await deactivatePet.HandleAsync(
            new DeactivatePetCommand(User.GetUserId(), CurrentRole(), petId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Reativa um pet desativado da conta logada. Idempotente.</summary>
    /// <remarks>
    /// Pet de outra conta: 404 <c>PET_NOT_FOUND</c>. Se outro pet ativo ficou com o mesmo microchip enquanto este estava desativado: 409
    /// <c>PET_MICROCHIP_ALREADY_REGISTERED</c>.
    /// </remarks>
    [HttpDelete("{petId:guid}/deactivation")]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReactivateAsync(Guid petId, CancellationToken cancellationToken)
    {
        var result = await reactivatePet.HandleAsync(
            new ReactivatePetCommand(User.GetUserId(), CurrentRole(), petId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Adiciona uma foto ao pet (até 3).</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem na parte <c>file</c>: JPEG, PNG ou WebP, até
    /// 5 MB (o formato é conferido pelo conteúdo, não pelo nome). A foto entra na primeira
    /// vaga livre; a primeira de <c>photos</c> é a capa. Com 3 fotos, 422
    /// <c>PET_PHOTO_LIMIT_REACHED</c>: substitua (<c>PATCH</c>) ou tire (<c>DELETE</c>) uma.
    /// Arquivo inválido: 400 no campo <c>file</c>. Pet de outra conta: 404 <c>PET_NOT_FOUND</c>.
    /// </remarks>
    [HttpPost("{petId:guid}/photos")]
    [Authorize(Roles = Keepers)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddPhotoAsync(Guid petId, IFormFile? file, CancellationToken cancellationToken)
    {
        var result = await addPetPhoto.HandleAsync(
            new AddPetPhotoCommand(User.GetUserId(), CurrentRole(), petId, file.ToImageUpload()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Substitui uma foto do pet.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem nova na parte <c>file</c>. A foto mantém o id e a
    /// posição; a imagem antiga é apagada do bucket. Foto que não é do pet: 404
    /// <c>PET_PHOTO_NOT_FOUND</c>. Pet de outra conta: 404 <c>PET_NOT_FOUND</c>.
    /// </remarks>
    [HttpPatch("{petId:guid}/photos/{photoId:guid}")]
    [Authorize(Roles = Keepers)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ReplacePhotoAsync(Guid petId, Guid photoId, IFormFile? file, CancellationToken cancellationToken)
    {
        var result = await replacePetPhoto.HandleAsync(
            new ReplacePetPhotoCommand(User.GetUserId(), CurrentRole(), petId, photoId, file.ToImageUpload()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Tira uma foto do pet.</summary>
    /// <remarks>
    /// A imagem é apagada do bucket; as outras fotos mantêm a posição. Foto que não é do pet:
    /// 404 <c>PET_PHOTO_NOT_FOUND</c>. Pet de outra conta: 404 <c>PET_NOT_FOUND</c>.
    /// </remarks>
    [HttpDelete("{petId:guid}/photos/{photoId:guid}")]
    [Authorize(Roles = Keepers)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<PetResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePhotoAsync(Guid petId, Guid photoId, CancellationToken cancellationToken)
    {
        var result = await removePetPhoto.HandleAsync(
            new RemovePetPhotoCommand(User.GetUserId(), CurrentRole(), petId, photoId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Papel do token. Cada conta tem um só.</summary>
    private string CurrentRole() =>
        User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Host) ? Roles.Host
        : Roles.Owner;
}
