using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;

/// <summary>Tira uma foto do cantinho do anfitrião logado; as de trás sobem uma posição.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="PhotoId">Vem da rota.</param>
public sealed record RemoveListingPhotoCommand(Guid UserId, Guid ListingId, Guid PhotoId) : ICommand<ListingResponse>;
