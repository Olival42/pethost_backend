using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;

/// <summary>Substitui a imagem de uma foto do cantinho, mantendo o id e a posição.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="PhotoId">Vem da rota: a foto a substituir.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record ReplaceListingPhotoCommand(
    Guid UserId,
    Guid ListingId,
    Guid PhotoId,
    ImageUpload? Image) : ICommand<ListingResponse>;
