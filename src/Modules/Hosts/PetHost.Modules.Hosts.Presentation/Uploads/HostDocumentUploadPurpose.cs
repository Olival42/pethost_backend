using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Presentation.Uploads;

/// <summary>
/// <c>host-document</c>: uma foto da verificação (<c>kind</c>: <c>front</c>, <c>back</c> ou
/// <c>selfie</c>) no rascunho do anfitrião, para o bucket <b>privado</b>. O mesmo caso de uso do
/// <c>PUT /hosts/me/application/documents</c>, com só a vaga desta foto: trocar a foto apaga a anterior.
/// </summary>
internal sealed class HostDocumentUploadPurpose(
    ICommandHandler<UploadHostApplicationDocumentsCommand, HostApplicationResponse> uploadDocuments) : IUploadPurpose
{
    private const string Front = "front";
    private const string Back = "back";
    private const string Selfie = "selfie";

    public string Name => "host-document";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Host];

    public UploadTargetRules Target { get; } = new(Kinds: [Front, Back, Selfie]);

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await uploadDocuments
            .HandleAsync(Command(context, ImageUpload.Preflight), cancellationToken)
            .ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await uploadDocuments
            .HandleAsync(Command(context, image), cancellationToken)
            .ConfigureAwait(false));

    private static UploadHostApplicationDocumentsCommand Command(UploadPurposeContext context, ImageUpload image) =>
        new(
            context.UserId,
            context.Kind == Front ? image : null,
            context.Kind == Back ? image : null,
            context.Kind == Selfie ? image : null);
}
