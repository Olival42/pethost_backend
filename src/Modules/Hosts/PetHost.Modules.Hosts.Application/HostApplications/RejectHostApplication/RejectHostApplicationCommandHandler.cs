using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;

/// <summary>
/// Recusa com motivo. Depois: bloqueio do CPF se a recusa fechou a identidade de vez (fraude ou
/// tentativas esgotadas), trilha, fotos apagadas conforme o prazo de guarda e aviso por e-mail
/// dizendo o motivo e se dá para tentar de novo.
/// </summary>
public sealed class RejectHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IIdentityBlockRepository identityBlocks,
    ICpfFingerprint fingerprint,
    IHostsUnitOfWork unitOfWork,
    IPrivateImageStorage storage,
    IHostVerificationPolicy policy,
    IHostAccounts accounts,
    IHostApplicationNotifier notifier,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RejectHostApplicationCommand, HostApplicationReviewResponse>
{
    public async Task<Result<HostApplicationReviewResponse>> HandleAsync(RejectHostApplicationCommand command, CancellationToken cancellationToken)
    {
        var id = new HostApplicationId(command.ApplicationId);
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return Result<HostApplicationReviewResponse>.Failure(HostsErrors.ApplicationNotFound(id));

        var reason = HostApplicationRules.ToRejectionReason(command.RejectionReason);

        var now = timeProvider.GetUtcNow();
        var rejected = application.Reject(command.AdminId, reason, command.Reason, now);
        if (rejected.IsFailure)
            return Result<HostApplicationReviewResponse>.FromFailure(rejected);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostApplicationReviewResponse>.FromFailure(saved);

        var history = await applicationRepository.ListByUserIdAsync(application.UserId, cancellationToken).ConfigureAwait(false);
        var blocked = await BlockIdentityIfDueAsync(application, history, now, cancellationToken).ConfigureAwait(false);

        var wireReason = RejectionReasonValues.ToWire(reason);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostApplicationRejected,
                    AuditTargets.HostApplication,
                    application.Id.Value,
                    command.Reason,
                    new Dictionary<string, string?>
                    {
                        ["rejectionReason"] = wireReason,
                        ["final"] = (application.Status == HostApplicationStatus.RejectedFinal).ToString().ToLowerInvariant(),
                        ["identityBlocked"] = blocked is { } block ? IdentityBlockReasonValues.ToWire(block) : null,
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        await HostApplicationSupport
            .PurgeDocumentsIfDueAsync(application, policy, storage, unitOfWork, now, cancellationToken)
            .ConfigureAwait(false);

        var account = await HostApplicationSupport.GetAccountAsync(accounts, application.UserId, cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            await notifier
                .NotifyRejectedAsync(
                    new HostApplicationDecision(account.Email, account.FullName, wireReason, HostApplication.CanStart(history)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostApplicationReviewResponse>.Success(HostApplicationReviewResponse.From(application, account));
    }

    /// <summary>
    /// Grava o bloqueio do CPF quando o histórico fecha a identidade (<see cref="HostApplication.IdentityBlockFor"/>).
    /// CPF já bloqueado fica como está. Devolve o motivo, se a identidade está bloqueada.
    /// </summary>
    private async Task<IdentityBlockReason?> BlockIdentityIfDueAsync(
        HostApplication application,
        IReadOnlyCollection<HostApplication> history,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (HostApplication.IdentityBlockFor(history) is not { } reason)
            return null;

        var print = fingerprint.Of(application.Cpf);
        if (await identityBlocks.FindAsync(print, cancellationToken).ConfigureAwait(false) is not null)
            return reason;

        var block = IdentityBlock.Create(print, reason, application.Id.Value, now);
        identityBlocks.Add(block);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsSuccess)
        {
            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.HostIdentityBlocked,
                        AuditTargets.HostApplication,
                        application.Id.Value,
                        Details: new Dictionary<string, string?>
                        {
                            ["cpf"] = application.Cpf.ToString(),
                            ["blockReason"] = IdentityBlockReasonValues.ToWire(reason),
                        }),
                    cancellationToken)
                .ConfigureAwait(false);

            return reason;
        }

        // Conflito: outra recusa bloqueou o mesmo CPF ao mesmo tempo. A linha sai do contexto para não
        // ir de novo nas próximas gravações; a recusa já foi gravada e o bloqueio da conta vale.
        identityBlocks.Remove(block);
        return await identityBlocks.FindAsync(print, cancellationToken).ConfigureAwait(false) is not null ? reason : null;
    }
}
