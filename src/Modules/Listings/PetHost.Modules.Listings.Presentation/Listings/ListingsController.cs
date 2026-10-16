using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Listings.Application.Listings.AddListingPhoto;
using PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;
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
    IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>> searchListings,
    ICommandHandler<BlockListingDatesCommand, AvailabilityBlockResponse> blockListingDates,
    ICommandHandler<UnblockListingDatesCommand, IReadOnlyList<AvailabilityBlockResponse>> unblockListingDates,
    IQueryHandler<ListAvailabilityBlocksQuery, IReadOnlyList<AvailabilityBlockResponse>> listAvailabilityBlocks) : ControllerBase
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
    /// <para>
    /// Datas: <c>checkIn</c> e <c>checkOut</c> (<c>yyyy-MM-dd</c>, juntos; mesmas regras do pedido
    /// de reserva) e <c>pets</c> (quantos vão, padrão 1): só cantinhos que recebem em todos os dias
    /// do período e com vaga para todos os pets em todas as noites. Cada item traz os selos em
    /// <c>badges</c> (<c>neighbor</c>, <c>fast_responder</c>, <c>yard</c>, <c>experienced</c>).
    /// </para>
    /// Só aparecem cantinhos ativos de anfitrião ativo. Cada item traz bairro, cidade e distância —
    /// nunca o endereço nem a coordenada. Paginado: <c>page</c> a partir de 1, <c>pageSize</c> até
    /// 100. Sem coordenada, a origem é o endereço de quem busca: o da empresa para o anfitrião
    /// empresa, senão o da conta (também é a base do selo <c>neighbor</c>). Busca por raio sem
    /// endereço: 422 <c>LISTING_SEARCH_ADDRESS_REQUIRED</c>.
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
                request.PageSize,
                request.CheckIn,
                request.CheckOut,
                request.Pets,
                CurrentRole()),
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
    /// Cantinho de outro anfitrião: 404 <c>LISTING_NOT_FOUND</c>. Com reserva em aberto
    /// (solicitada, aceita, confirmada ou em andamento): 422 <c>LISTING_HAS_OPEN_BOOKING</c> —
    /// responda, cancele ou faça o check-out antes.
    /// </remarks>
    [HttpPost("{listingId:guid}/deactivation")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ListingResponse>>(StatusCodes.Status422UnprocessableEntity)]
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

    /// <summary>A agenda do cantinho: os períodos fechados pelo anfitrião que ainda não acabaram.</summary>
    /// <remarks>
    /// Por padrão tudo está disponível; cada item fecha os dias de <c>startDate</c> a
    /// <c>endDate</c> (inclusive). Quem vê é quem vê o cantinho: o dono e o admin, com o
    /// <c>reason</c>; qualquer outra conta, só as datas, de cantinho ativo e disponível. Fora isso:
    /// 404 <c>LISTING_NOT_FOUND</c>.
    /// </remarks>
    [HttpGet("{listingId:guid}/availability-blocks")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAvailabilityBlocksAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var result = await listAvailabilityBlocks.HandleAsync(
            new ListAvailabilityBlocksQuery(User.GetUserId(), CurrentRole(), listingId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Fecha datas na agenda do cantinho do anfitrião logado.</summary>
    /// <remarks>
    /// Corpo: <c>{ "startDate": "2026-12-20", "endDate": "2026-12-26", "reason": "Viagem" }</c>
    /// (<c>reason</c> opcional, até 120 caracteres, só para o anfitrião). Início a partir de hoje
    /// (Brasília), fim não antes do início, até 365 dias à frente — todos os erros num só 400.
    /// Nenhum pedido de reserva passa por esses dias (422 <c>BOOKING_DATES_BLOCKED</c>) e o
    /// cantinho some da busca com essas datas. Reservas já pedidas ou aceitas continuam. Período
    /// que sobrepõe outro bloqueio: 422 <c>LISTING_AVAILABILITY_BLOCK_OVERLAP</c>; mais de 50 em
    /// aberto: 422 <c>LISTING_AVAILABILITY_BLOCK_LIMIT_REACHED</c>. Cantinho de outro anfitrião:
    /// 404 <c>LISTING_NOT_FOUND</c>.
    /// </remarks>
    [HttpPost("{listingId:guid}/availability-blocks")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<AvailabilityBlockResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BlockDatesAsync(
        Guid listingId,
        [FromBody] AvailabilityBlockRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await blockListingDates.HandleAsync(
            new BlockListingDatesCommand(User.GetUserId(), listingId, request.StartDate, request.EndDate, request.Reason),
            cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult($"/api/v1/listings/{listingId}/availability-blocks")
            : result.ToActionResult();
    }

    /// <summary>Reabre as datas de um bloqueio da agenda. Devolve a agenda como ficou.</summary>
    /// <remarks>Bloqueio que não é do cantinho: 404 <c>LISTING_AVAILABILITY_BLOCK_NOT_FOUND</c>.</remarks>
    [HttpDelete("{listingId:guid}/availability-blocks/{blockId:guid}")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AvailabilityBlockResponse>>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockDatesAsync(Guid listingId, Guid blockId, CancellationToken cancellationToken)
    {
        var result = await unblockListingDates.HandleAsync(
            new UnblockListingDatesCommand(User.GetUserId(), listingId, blockId),
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
