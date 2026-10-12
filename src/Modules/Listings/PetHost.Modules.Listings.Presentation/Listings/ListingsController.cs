using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Listings.Application.Listings.AddListingPhoto;
using PetHost.Modules.Listings.Application.Listings.CreateListing;
using PetHost.Modules.Listings.Application.Listings.DeactivateListing;
using PetHost.Modules.Listings.Application.Listings.GetListingById;
using PetHost.Modules.Listings.Application.Listings.ListMyListings;
using PetHost.Modules.Listings.Application.Listings.ReactivateListing;
using PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;
using PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;
using PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Application.Listings.SearchListings;
using PetHost.Modules.Listings.Application.Listings.UpdateListing;
using PetHost.Modules.Listings.Application.Listings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Presentation.Listings;

/// <summary>
/// Cantinhos: o espaço que o anfitrião oferece. O anfitrião sai sempre do token — nunca do
/// corpo. Pessoa física tem um cantinho; empresa, um por filial.
/// </summary>
[ApiController]
[Route("api/v1/listings")]
public sealed class ListingsController(
    ICommandHandler<CreateListingCommand, ListingResponse> createListing,
    ICommandHandler<UpdateListingCommand, ListingResponse> updateListing,
    ICommandHandler<DeactivateListingCommand, ListingResponse> deactivateListing,
    ICommandHandler<ReactivateListingCommand, ListingResponse> reactivateListing,
    ICommandHandler<AddListingPhotoCommand, ListingResponse> addListingPhoto,
    ICommandHandler<ReplaceListingPhotoCommand, ListingResponse> replaceListingPhoto,
    ICommandHandler<RemoveListingPhotoCommand, ListingResponse> removeListingPhoto,
    ICommandHandler<ReorderListingPhotosCommand, ListingResponse> reorderListingPhotos,
    IQueryHandler<GetListingByIdQuery, ListingResponse> getListingById,
    IQueryHandler<ListMyListingsQuery, MyListingsResponse> listMyListings,
    IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>> searchListings) : ControllerBase
{
    /// <summary>Cadastra um cantinho do anfitrião logado.</summary>
    /// <remarks>
    /// Obrigatórios: <c>title</c>, <c>description</c>, <c>hasYard</c>, <c>hasDogs</c>, <c>hasCats</c>,
    /// <c>hasKids</c>, <c>acceptedSpecies</c>, <c>acceptsSmall</c>, <c>acceptsMedium</c>,
    /// <c>acceptsLarge</c>, <c>capacity</c>, <c>nightlyRate</c>, <c>availableWeekdays</c> e o
    /// <c>address</c> (exceto o complemento); <c>homeType</c> obrigatório só para pessoa física.
    /// Todos os erros voltam juntos num só 400. A coordenada é calculada a partir do endereço.
    /// Conta sem anfitrião verificado: 404 <c>LISTING_HOST_NOT_FOUND</c>. Anfitrião inativo ou
    /// suspenso: 403 <c>LISTING_HOST_DEACTIVATED</c>. Stripe ainda sem aprovar o cadastro de
    /// recebimento (<c>/hosts/me/stripe-account</c>): 422 <c>LISTING_HOST_PAYOUTS_NOT_ENABLED</c>.
    /// Pessoa física que já tem cantinho: 422
    /// <c>LISTING_LIMIT_REACHED</c>. Endereço não achado no mapa: 422
    /// <c>LISTING_ADDRESS_NOT_LOCATED</c>. Mapa fora do ar: 503 <c>LISTING_GEOCODER_UNAVAILABLE</c>.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAsync([FromBody] ListingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateListingCommand(
            User.GetUserId(),
            request.Title,
            request.Description,
            request.HomeType,
            request.HasYard,
            request.HasDogs,
            request.HasCats,
            request.HasKids,
            request.AcceptedSpecies,
            request.AcceptsSmall,
            request.AcceptsMedium,
            request.AcceptsLarge,
            request.Capacity,
            request.NightlyRate,
            request.HouseRules,
            request.AvailableWeekdays,
            ToAddressData(request.Address));

        var result = await createListing.HandleAsync(command, cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult($"/api/v1/listings/{result.Value!.Id}")
            : result.ToActionResult();
    }

    /// <summary>Busca cantinhos com filtros que se somam. Qualquer conta logada.</summary>
    /// <remarks>
    /// Todos os filtros são opcionais e se somam (o cantinho atende a cada um): <c>state</c> (UF),
    /// <c>city</c> e <c>neighborhood</c> (nome exato, sem diferença de acento nem de maiúsculas); <c>species</c>,
    /// <c>sizes</c> (<c>small</c>/<c>medium</c>/<c>large</c>) e <c>weekdays</c> — repetidos
    /// (<c>species=dog&amp;species=cat</c>) ou separados por vírgula, o cantinho precisa aceitar
    /// <b>todos</b>; <c>minNightlyRate</c>/<c>maxNightlyRate</c>; <c>minCapacity</c>; <c>hasYard</c>; <c>homeType</c> (<c>house</c>/<c>apartment</c>; cantinho de empresa, sem tipo, não entra);
    /// <c>hasDogs</c>, <c>hasCats</c> e <c>hasKids</c> (quem mora na casa: <c>true</c> ou <c>false</c>).
    /// <para>
    /// Distância: com <c>radiusKm</c> (5 a 50) ou <c>latitude</c>+<c>longitude</c>, ou sem
    /// estado/cidade/bairro, é busca por raio (padrão 10 km), do mais perto ao mais longe; a origem
    /// é a coordenada enviada ou, sem ela, o endereço da conta logada. Só com estado/cidade/bairro,
    /// não há raio: vem do mais novo ao mais antigo, sem <c>distanceKm</c>.
    /// </para>
    /// Só aparecem cantinhos ativos de anfitrião ativo. Cada item traz bairro, cidade e distância —
    /// nunca o endereço nem a coordenada. Paginado: <c>page</c> a partir de 1, <c>pageSize</c> até
    /// 100. Busca por raio com conta sem endereço: 422 <c>LISTING_SEARCH_ADDRESS_REQUIRED</c>.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<ApiResponse<PagedResult<ListingSummaryResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ListingSummaryResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ListingSummaryResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ListingSummaryResponse>>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<PagedResult<ListingSummaryResponse>>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] ListingSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await searchListings.HandleAsync(
            new SearchListingsQuery(
                User.GetUserId(),
                request.Latitude,
                request.Longitude,
                request.RadiusKm,
                request.State,
                request.City,
                request.Neighborhood,
                request.Species,
                request.Sizes,
                request.Weekdays,
                request.MinNightlyRate,
                request.MaxNightlyRate,
                request.MinCapacity,
                request.HasYard,
                request.HomeType,
                request.HasDogs,
                request.HasCats,
                request.HasKids,
                request.Page,
                request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Todos os cantinhos do anfitrião logado, ativos e inativos. Sem paginação nem filtro.</summary>
    /// <remarks>O anfitrião vem uma vez, em <c>host</c>; os cantinhos, em <c>listings</c>, com o endereço completo.</remarks>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<MyListingsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MyListingsResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MyListingsResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MyListingsResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken)
    {
        var result = await listMyListings.HandleAsync(new ListMyListingsQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Um cantinho, com o anfitrião.</summary>
    /// <remarks>
    /// O anfitrião dono vê sempre (inclusive inativo), com endereço completo e coordenada; o
    /// admin vê todos. Qualquer outra conta logada vê o cantinho ativo de anfitrião ativo, só
    /// com bairro, cidade e UF. Fora isso: 404 <c>LISTING_NOT_FOUND</c>, como se não existisse.
    /// </remarks>
    [HttpGet("{listingId:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var result = await getListingById.HandleAsync(new GetListingByIdQuery(User.GetUserId(), CurrentRole(), listingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Altera um cantinho do anfitrião logado (PATCH: só o que vier muda).</summary>
    /// <remarks>
    /// Campo ausente ou <c>null</c> não mexe; <c>""</c> limpa um campo opcional; lista
    /// (<c>acceptedSpecies</c>, <c>availableWeekdays</c>) substitui a atual. O endereço é parcial
    /// e, se mudar, a coordenada é recalculada. O resultado é validado inteiro: todos os erros num
    /// só 400. Cantinho de outro anfitrião: 404 <c>LISTING_NOT_FOUND</c>. Cantinho inativo também
    /// pode ser editado.
    /// </remarks>
    [HttpPatch("{listingId:guid}")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateAsync(
        Guid listingId,
        [FromBody] ListingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateListingCommand(
            User.GetUserId(),
            listingId,
            request.Title,
            request.Description,
            request.HomeType,
            request.HasYard,
            request.HasDogs,
            request.HasCats,
            request.HasKids,
            request.AcceptedSpecies,
            request.AcceptsSmall,
            request.AcceptsMedium,
            request.AcceptsLarge,
            request.Capacity,
            request.NightlyRate,
            request.HouseRules,
            request.AvailableWeekdays,
            ToAddressData(request.Address));

        var result = await updateListing.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Inativa um cantinho do anfitrião logado. Idempotente.</summary>
    /// <remarks>
    /// Inativo, some da busca e de quem não é o dono, e pode voltar com <c>DELETE</c> nesta rota.
    /// Cantinho de outro anfitrião: 404 <c>LISTING_NOT_FOUND</c>.
    /// </remarks>
    [HttpPost("{listingId:guid}/deactivation")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var result = await deactivateListing.HandleAsync(new DeactivateListingCommand(User.GetUserId(), listingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Reativa um cantinho inativo do anfitrião logado. Idempotente.</summary>
    /// <remarks>
    /// Cantinho de outro anfitrião: 404 <c>LISTING_NOT_FOUND</c>. Anfitrião inativo ou suspenso:
    /// 403 <c>LISTING_HOST_DEACTIVATED</c>.
    /// </remarks>
    [HttpDelete("{listingId:guid}/deactivation")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var result = await reactivateListing.HandleAsync(new ReactivateListingCommand(User.GetUserId(), listingId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Adiciona uma foto ao cantinho (até 5), no fim da fila.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem na parte <c>file</c>: JPEG, PNG ou WebP, até 5 MB
    /// (o formato é conferido pelo conteúdo). A foto entra na próxima posição; a posição 1 é a
    /// capa. Com 5 fotos: 422 <c>LISTING_PHOTO_LIMIT_REACHED</c>. Imagem repetida: 409
    /// <c>LISTING_PHOTO_ALREADY_EXISTS</c>. Arquivo inválido: 400 no campo <c>file</c>.
    /// </remarks>
    [HttpPost("{listingId:guid}/photos")]
    [Authorize(Roles = Roles.Host)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddPhotoAsync(Guid listingId, IFormFile? file, CancellationToken cancellationToken)
    {
        var result = await addListingPhoto.HandleAsync(
            new AddListingPhotoCommand(User.GetUserId(), listingId, file.ToImageUpload()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Reordena as fotos do cantinho: a primeira da lista vira a capa.</summary>
    /// <remarks>
    /// Corpo: <c>{ "photoIds": [...] }</c> com <b>todas</b> as fotos do cantinho, cada uma uma vez,
    /// na ordem nova (posição 1 até a 5). Id que não é foto deste cantinho: 404
    /// <c>LISTING_PHOTO_NOT_FOUND</c>. Lista incompleta ou com repetição: 400 no campo <c>photoIds</c>.
    /// </remarks>
    [HttpPut("{listingId:guid}/photos/order")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderPhotosAsync(
        Guid listingId,
        [FromBody] ReorderPhotosRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await reorderListingPhotos.HandleAsync(
            new ReorderListingPhotosCommand(User.GetUserId(), listingId, request.PhotoIds),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Substitui uma foto do cantinho.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem nova na parte <c>file</c>. A foto mantém o id e a
    /// posição; a imagem antiga é apagada do bucket. Foto que não é do cantinho: 404
    /// <c>LISTING_PHOTO_NOT_FOUND</c>.
    /// </remarks>
    [HttpPatch("{listingId:guid}/photos/{photoId:guid}")]
    [Authorize(Roles = Roles.Host)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ReplacePhotoAsync(Guid listingId, Guid photoId, IFormFile? file, CancellationToken cancellationToken)
    {
        var result = await replaceListingPhoto.HandleAsync(
            new ReplaceListingPhotoCommand(User.GetUserId(), listingId, photoId, file.ToImageUpload()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Tira uma foto do cantinho.</summary>
    /// <remarks>
    /// A imagem é apagada do bucket e as fotos de trás sobem uma posição. Foto que não é do
    /// cantinho: 404 <c>LISTING_PHOTO_NOT_FOUND</c>.
    /// </remarks>
    [HttpDelete("{listingId:guid}/photos/{photoId:guid}")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePhotoAsync(Guid listingId, Guid photoId, CancellationToken cancellationToken)
    {
        var result = await removeListingPhoto.HandleAsync(
            new RemoveListingPhotoCommand(User.GetUserId(), listingId, photoId),
            cancellationToken);

        return result.ToActionResult();
    }

    private static ListingAddressInput? ToAddressData(ListingAddressRequest? address) =>
        address is null
            ? null
            : new ListingAddressInput(address.ZipCode, address.Street, address.Number, address.Complement, address.Neighborhood, address.City, address.State);

    /// <summary>Papel do token. Cada conta tem um só.</summary>
    private string CurrentRole() =>
        User.IsInRole(Roles.Admin) ? Roles.Admin
        : User.IsInRole(Roles.Host) ? Roles.Host
        : Roles.Owner;
}
