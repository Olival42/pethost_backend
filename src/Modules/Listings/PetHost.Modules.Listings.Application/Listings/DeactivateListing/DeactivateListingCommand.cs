using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.DeactivateListing;

/// <summary>
/// Inativa um cantinho do anfitrião logado (férias, reforma, filial fechada). Cantinho nunca é
/// apagado; inativo, some da busca e de quem não é o dono, e pode voltar.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
public sealed record DeactivateListingCommand(Guid UserId, Guid ListingId) : ICommand<ListingResponse>;
