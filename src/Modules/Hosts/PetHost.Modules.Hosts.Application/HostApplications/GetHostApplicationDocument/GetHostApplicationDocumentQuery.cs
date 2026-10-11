using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationDocument;

/// <summary>Uma foto da solicitação (documento ou selfie), para o admin ver.</summary>
/// <param name="Kind"><c>front</c>, <c>back</c> ou <c>selfie</c> (vem da rota).</param>
public sealed record GetHostApplicationDocumentQuery(Guid ApplicationId, string? Kind) : IQuery<HostApplicationDocumentLinkResponse>;

/// <summary>
/// Gera a URL assinada da foto no bucket privado, que vence em poucos minutos. Cada link
/// emitido vai para a trilha: é dado pessoal sensível (selfie, documento).
/// </summary>
public sealed class GetHostApplicationDocumentQueryHandler(
    IHostApplicationRepository applicationRepository,
    IPrivateImageStorage storage,
    IAuditTrail auditTrail) : IQueryHandler<GetHostApplicationDocumentQuery, HostApplicationDocumentLinkResponse>
{
    public async Task<Result<HostApplicationDocumentLinkResponse>> HandleAsync(GetHostApplicationDocumentQuery query, CancellationToken cancellationToken)
    {
        if (!DocumentKindValues.TryParse(query.Kind, out var kind))
            return Result<HostApplicationDocumentLinkResponse>.Failure(HostsErrors.DocumentKindInvalid);

        var id = new HostApplicationId(query.ApplicationId);
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return Result<HostApplicationDocumentLinkResponse>.Failure(HostsErrors.ApplicationNotFound(id));

        var document = application.Documents.FirstOrDefault(d => d.Kind == kind);
        if (document is null)
            return Result<HostApplicationDocumentLinkResponse>.Failure(HostsErrors.DocumentNotFound);

        var link = await storage.GetSignedUrlAsync(document.StorageKey, cancellationToken).ConfigureAwait(false);
        if (link is null)
            return Result<HostApplicationDocumentLinkResponse>.Failure(HostsErrors.DocumentNotFound);

        var wireKind = DocumentKindValues.ToWire(kind);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostApplicationDocumentViewed,
                    AuditTargets.HostApplication,
                    application.Id.Value,
                    Details: new Dictionary<string, string?> { ["kind"] = wireKind }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationDocumentLinkResponse>.Success(
            new HostApplicationDocumentLinkResponse(wireKind, link.Url, link.ExpiresAt.ToUnixTimeSeconds()));
    }
}
