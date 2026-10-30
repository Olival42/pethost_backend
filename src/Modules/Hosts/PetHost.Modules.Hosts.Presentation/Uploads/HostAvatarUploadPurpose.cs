using PetHost.Modules.Hosts.Application.Hosts.ChangeMyHostAvatar;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Presentation.Uploads;

/// <summary><c>host-avatar</c>: foto de perfil do anfitrião. O mesmo caso de uso do <c>PUT /hosts/me/avatar</c>.</summary>
internal sealed class HostAvatarUploadPurpose(
    ICommandHandler<ChangeMyHostAvatarCommand, HostResponse> changeMyAvatar) : IUploadPurpose
{
    public string Name => "host-avatar";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Host];

    public UploadTargetRules Target => UploadTargetRules.None;

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await changeMyAvatar
            .HandleAsync(new ChangeMyHostAvatarCommand(context.UserId, ImageUpload.Preflight), cancellationToken)
            .ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await changeMyAvatar
            .HandleAsync(new ChangeMyHostAvatarCommand(context.UserId, image), cancellationToken)
            .ConfigureAwait(false));
}
