using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;

/// <summary>
/// Recusa com motivo. Depois: trilha, fotos apagadas conforme o prazo de guarda e aviso por
/// e-mail dizendo o motivo e se dá para tentar de novo.
/// </summary>
public sealed class RejectHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
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
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        await HostApplicationSupport
            .PurgeDocumentsIfDueAsync(application, policy, storage, unitOfWork, now, cancellationToken)
            .ConfigureAwait(false);

        var account = await HostApplicationSupport.GetAccountAsync(accounts, application.UserId, cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            var history = await applicationRepository.ListByUserIdAsync(application.UserId, cancellationToken).ConfigureAwait(false);

            await notifier
                .NotifyRejectedAsync(
                    new HostApplicationDecision(account.Email, account.FullName, wireReason, HostApplication.CanStart(history)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostApplicationReviewResponse>.Success(HostApplicationReviewResponse.From(application, account));
    }
}
