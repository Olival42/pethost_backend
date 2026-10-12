using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.CreateListing;

/// <summary>
/// Cadastra um cantinho do anfitrião logado. Pessoa física tem um só; empresa, um por filial.
/// A coordenada sai do endereço (geocodificada pela API), nunca do corpo.
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="HomeType"><c>house</c> ou <c>apartment</c>. Obrigatório para pessoa física; opcional para empresa.</param>
/// <param name="NightlyRate">Diária por pet, em reais, até 2 casas.</param>
public sealed record CreateListingCommand(
    Guid UserId,
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
    ListingAddressInput? Address) : ICommand<ListingResponse>
{
    public ListingDetailsData ToDetailsData() =>
        new(
            Title,
            Description,
            HomeType,
            HasYard,
            HasDogs,
            HasCats,
            HasKids,
            AcceptedSpecies,
            AcceptsSmall,
            AcceptsMedium,
            AcceptsLarge,
            Capacity,
            NightlyRate,
            HouseRules,
            AvailableWeekdays);
}
