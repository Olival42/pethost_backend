using PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Presentation.Uploads;

/// <summary>
/// <c>owner-avatar</c>: foto de perfil do tutor, pelo upload direto. O mesmo caso de uso do
/// <c>PUT /owners/me/avatar</c>: a foto anterior só é apagada depois que a nova foi gravada.
/// </summary>
internal sealed class OwnerAvatarUploadPurpose(
    ICommandHandler<ChangeMyAvatarCommand, OwnerResponse> changeMyAvatar) : IUploadPurpose
{
    public string Name => "owner-avatar";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Owner];

    public UploadTargetRules Target => UploadTargetRules.None;

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await changeMyAvatar
            .HandleAsync(new ChangeMyAvatarCommand(context.UserId, ImageUpload.Preflight), cancellationToken)
            .ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await changeMyAvatar
            .HandleAsync(new ChangeMyAvatarCommand(context.UserId, image), cancellationToken)
            .ConfigureAwait(false));
}
