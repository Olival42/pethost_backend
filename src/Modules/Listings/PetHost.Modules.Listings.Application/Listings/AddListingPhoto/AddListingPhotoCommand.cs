using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.AddListingPhoto;

/// <summary>Adiciona uma foto ao cantinho do anfitrião logado (até 5), no fim da fila.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record AddListingPhotoCommand(Guid UserId, Guid ListingId, ImageUpload? Image) : ICommand<ListingResponse>;
