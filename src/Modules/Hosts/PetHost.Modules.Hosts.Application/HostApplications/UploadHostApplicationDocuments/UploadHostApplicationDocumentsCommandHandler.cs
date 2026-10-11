using System.Globalization;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;

/// <summary>
/// Envia as fotos ao bucket <b>privado</b>, grava as chaves nas vagas e apaga as fotos que
/// foram trocadas. Tudo ou nada: se uma foto falhar (formato, tamanho, repetida), nenhuma
/// entra e as que já subiram são apagadas. Bucket e banco não têm uma transação só, então a
/// ordem é a de sempre: confere o rascunho antes de enviar; se gravar falhar, apaga os
/// arquivos novos (compensação).
/// </summary>
public sealed class UploadHostApplicationDocumentsCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IPrivateImageStorage storage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UploadHostApplicationDocumentsCommand, HostApplicationResponse>
{
    public async Task<Result<HostApplicationResponse>> HandleAsync(UploadHostApplicationDocumentsCommand command, CancellationToken cancellationToken)
    {
        var open = await HostApplicationSupport.GetOpenAsync(applicationRepository, command.UserId, cancellationToken).ConfigureAwait(false);
        if (open.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(open);

        var application = open.Value!;

        // Sem rascunho editável, nada vai para o bucket.
        var accepts = application.EnsureAcceptsDocuments();
        if (accepts.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(accepts);

        var sent = new (DocumentKind Kind, ImageUpload? Image)[]
            {
                (DocumentKind.Front, command.Front),
                (DocumentKind.Back, command.Back),
                (DocumentKind.Selfie, command.Selfie),
            }
            .Where(part => part.Image is not null)
            .ToList();

        var stored = new List<NewHostApplicationDocument>(sent.Count);
        Result<IReadOnlyList<string>> attached;
        try
        {
            foreach (var (kind, image) in sent)
            {
                var saved = await storage.SaveAsync(image, PrivateImageFolders.HostApplications, cancellationToken).ConfigureAwait(false);
                if (saved.IsFailure)
                {
                    await DeleteAsync(stored).ConfigureAwait(false);
                    return Result<HostApplicationResponse>.Failure(InField(saved.Errors!, kind));
                }

                stored.Add(new NewHostApplicationDocument(kind, saved.Value!.Key, saved.Value.ContentHash, saved.Value.SizeBytes));
            }

            attached = application.AttachDocuments(stored, timeProvider.GetUtcNow());
            if (attached.IsSuccess)
            {
                var savedChanges = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (savedChanges.IsFailure)
                    attached = Result<IReadOnlyList<string>>.FromFailure(savedChanges);
            }
        }
        catch
        {
            await DeleteAsync(stored).ConfigureAwait(false);
            throw;
        }

        if (attached.IsFailure)
        {
            await DeleteAsync(stored).ConfigureAwait(false);
            return Result<HostApplicationResponse>.FromFailure(attached);
        }

        foreach (var replacedKey in attached.Value!)
            await storage.DeleteAsync(replacedKey, CancellationToken.None).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostApplicationDocumentUploaded,
                    AuditTargets.HostApplication,
                    application.Id.Value,
                    Details: new Dictionary<string, string?>
                    {
                        ["kinds"] = string.Join(",", stored.Select(d => DocumentKindValues.ToWire(d.Kind))),
                        ["replaced"] = attached.Value.Count.ToString(CultureInfo.InvariantCulture),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationResponse>.Success(HostApplicationResponse.From(application));
    }

    // CancellationToken.None: a limpeza roda mesmo se o pedido caiu.
    private async Task DeleteAsync(IEnumerable<NewHostApplicationDocument> documents)
    {
        foreach (var document in documents)
            await storage.DeleteAsync(document.StorageKey, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// O bucket fala do campo <c>file</c>; aqui cada foto tem a sua parte, então o erro aponta
    /// a parte certa (<c>front</c>, <c>back</c> ou <c>selfie</c>).
    /// </summary>
    private static IEnumerable<Error> InField(IEnumerable<Error> errors, DocumentKind kind) =>
        errors.Select(error => error.Field == ImageErrors.Field ? error with { Field = DocumentKindValues.ToWire(kind) } : error);
}
