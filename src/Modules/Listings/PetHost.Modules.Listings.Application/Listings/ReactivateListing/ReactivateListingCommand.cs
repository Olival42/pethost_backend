using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.ReactivateListing;

/// <summary>Reativa um cantinho inativo do anfitrião logado: volta para a busca.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
public sealed record ReactivateListingCommand(Guid UserId, Guid ListingId) : ICommand<ListingResponse>;
