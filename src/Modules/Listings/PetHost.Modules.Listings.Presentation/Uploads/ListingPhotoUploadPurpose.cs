using PetHost.Modules.Listings.Application.Listings.AddListingPhoto;
using PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Presentation.Uploads;

/// <summary>
/// <c>listing-photo</c>: foto do cantinho (<c>resourceId</c> = <c>listingId</c>). Sem
/// <c>photoId</c>, adiciona no fim da fila (até 5); com <c>photoId</c>, substitui aquela foto. Os
/// mesmos casos de uso do <c>POST</c>/<c>PATCH /listings/{listingId}/photos</c>.
/// </summary>
internal sealed class ListingPhotoUploadPurpose(
    ICommandHandler<AddListingPhotoCommand, ListingResponse> addListingPhoto,
    ICommandHandler<ReplaceListingPhotoCommand, ListingResponse> replaceListingPhoto) : IUploadPurpose
{
    public string Name => "listing-photo";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Host];

    public UploadTargetRules Target { get; } = new(Resource: "listingId", AllowsPhotoId: true);

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await HandleAsync(context, ImageUpload.Preflight, cancellationToken).ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await HandleAsync(context, image, cancellationToken).ConfigureAwait(false));

    private Task<Result<ListingResponse>> HandleAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        context.PhotoId is { } photoId
            ? replaceListingPhoto.HandleAsync(new ReplaceListingPhotoCommand(context.UserId, context.ResourceId!.Value, photoId, image), cancellationToken)
            : addListingPhoto.HandleAsync(new AddListingPhotoCommand(context.UserId, context.ResourceId!.Value, image), cancellationToken);
}
