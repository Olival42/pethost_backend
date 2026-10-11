using System.Globalization;
using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.PurgeHostApplications;

/// <summary>
/// Uma rodada da tarefa de limpeza: fecha rascunhos abandonados e apaga as fotos das
/// solicitações fechadas cujo prazo de guarda passou. Disparada em segundo plano, sem usuário.
/// </summary>
public sealed record PurgeHostApplicationsCommand : ICommand<PurgeHostApplicationsResponse>;

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class PurgeHostApplicationsCommandValidator : AbstractValidator<PurgeHostApplicationsCommand>;

public sealed class PurgeHostApplicationsCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IPrivateImageStorage storage,
    IHostVerificationPolicy policy,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<PurgeHostApplicationsCommand, PurgeHostApplicationsResponse>
{
    public async Task<Result<PurgeHostApplicationsResponse>> HandleAsync(PurgeHostApplicationsCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // 1. Rascunhos abandonados viram "expired" (e entram no passo 2 se o prazo for zero).
        var drafts = await applicationRepository
            .ListDraftsCreatedBeforeAsync(now - policy.DraftLifetime, cancellationToken)
            .ConfigureAwait(false);

        var expired = drafts.Where(d => d.Expire(now)).ToList();
        if (expired.Count > 0)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<PurgeHostApplicationsResponse>.FromFailure(saved);

            foreach (var application in expired)
            {
                await auditTrail
                    .RecordAsync(new AuditRecord(AuditActions.HostApplicationExpired, AuditTargets.HostApplication, application.Id.Value), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // 2. Fotos de solicitações fechadas há mais que o prazo de guarda.
        var due = await applicationRepository
            .ListWithDocumentsClosedBeforeAsync(now - policy.DocumentRetention, cancellationToken)
            .ConfigureAwait(false);

        var purged = 0;
        foreach (var application in due)
        {
            var deleted = await HostApplicationSupport
                .PurgeDocumentsIfDueAsync(application, policy, storage, unitOfWork, now, cancellationToken)
                .ConfigureAwait(false);

            if (deleted == 0)
                continue;

            purged++;
            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.HostApplicationDocumentsPurged,
                        AuditTargets.HostApplication,
                        application.Id.Value,
                        Details: new Dictionary<string, string?> { ["count"] = deleted.ToString(CultureInfo.InvariantCulture) }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<PurgeHostApplicationsResponse>.Success(new PurgeHostApplicationsResponse(expired.Count, purged));
    }
}
