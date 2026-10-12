using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.UpdateListing;

/// <summary>
/// Altera um cantinho do anfitrião logado (PATCH): só muda o que vier. <c>null</c> mantém o
/// valor atual; texto vazio (<c>""</c>) limpa um campo opcional; lista (tipos, dias) que vier
/// substitui a atual inteira. O endereço também é parcial; se mudar, a coordenada é
/// recalculada. As fotos têm rotas próprias.
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="ListingId">Vem da rota.</param>
public sealed record UpdateListingCommand(
    Guid UserId,
    Guid ListingId,
    string? Title,
    string? Description,
    string? HomeType,
    bool? HasYard,
    bool? HasDogs,
    bool? HasCats,
    bool? HasKids,
    IReadOnlyList<string?>? AcceptedSpecies,
    bool? AcceptsSmall,
    bool? AcceptsMedium,
    bool? AcceptsLarge,
    int? Capacity,
    decimal? NightlyRate,
    string? HouseRules,
    IReadOnlyList<string?>? AvailableWeekdays,
    ListingAddressInput? Address) : ICommand<ListingResponse>;
