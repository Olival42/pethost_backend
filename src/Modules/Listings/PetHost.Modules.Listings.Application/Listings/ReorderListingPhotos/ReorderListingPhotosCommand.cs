using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;

/// <summary>
/// Põe as fotos do cantinho na ordem enviada: a primeira vira a capa (posição 1), a última fica
/// na posição n (até 5).
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">Vem da rota.</param>
/// <param name="PhotoIds">Todas as fotos do cantinho, cada uma uma vez, na ordem nova.</param>
public sealed record ReorderListingPhotosCommand(
    Guid UserId,
    Guid ListingId,
    IReadOnlyList<Guid>? PhotoIds) : ICommand<ListingResponse>;
