using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;
using System.Text.Json;
using System.Text.Json.Nodes;
using PetHost.Modules.Conversations.Application.Messages.GetMessagePhoto;

namespace PetHost.Modules.Conversations.Presentation.Uploads;

/// <summary>
/// <c>chat-photo</c>: foto no chat (<c>resourceId</c> = <c>conversationId</c>), com a legenda no
/// <c>body</c> da confirmação. A mensagem só existe (e o hub só avisa) depois da confirmação.
/// O id da mensagem é o id do upload: confirmar de novo nunca cria uma segunda mensagem.
/// </summary>
internal sealed class ChatPhotoUploadPurpose(
    ICommandHandler<SendPhotoMessageCommand, MessageResponse> sendPhotoMessage,
    IQueryHandler<GetMessagePhotoQuery, MessagePhotoResponse> getPhoto) : IUploadPurpose
{
    public string Name => "chat-photo";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Owner, Shared.Contracts.Authorization.Roles.Host];

    public UploadTargetRules Target { get; } = new(Resource: "conversationId");

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await HandleAsync(context, ImageUpload.Preflight, cancellationToken).ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await HandleAsync(context, image, cancellationToken).ConfigureAwait(false));

    public async Task<Result<object>> RestoreAsync(UploadPurposeContext context, JsonElement result, CancellationToken cancellationToken)
    {
        var photo = await getPhoto.HandleAsync(new GetMessagePhotoQuery(
            context.UserId, context.Role, context.ResourceId!.Value, context.UploadId), cancellationToken).ConfigureAwait(false);
        if (photo.IsFailure)
            return Result<object>.FromFailure(photo);
        var restored = JsonNode.Parse(result.GetRawText())!.AsObject();
        restored["imageUrl"] = photo.Value!.Url;
        restored["imageUrlExpiresAt"] = photo.Value.ExpiresAt;
        return Result<object>.Success(restored);
    }

    private Task<Result<MessageResponse>> HandleAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        sendPhotoMessage.HandleAsync(
            new SendPhotoMessageCommand(context.UserId, context.Role, context.ResourceId!.Value, image, context.Body, MessageId: context.UploadId),
            cancellationToken);
}
