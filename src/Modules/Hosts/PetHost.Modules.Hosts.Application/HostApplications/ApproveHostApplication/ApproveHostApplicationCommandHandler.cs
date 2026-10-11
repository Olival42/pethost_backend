using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.ApproveHostApplication;

/// <summary>
/// Aprova e cria o anfitrião numa transação só (mesmo schema). Dois admins decidindo ao
/// mesmo tempo: o segundo recebe <c>409 HOST_APPLICATION_CONFLICT</c>. Depois: trilha, fotos
/// apagadas conforme o prazo de guarda e aviso por e-mail.
/// </summary>
public sealed class ApproveHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IPrivateImageStorage storage,
    IHostVerificationPolicy policy,
    IHostAccounts accounts,
    IHostApplicationNotifier notifier,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ApproveHostApplicationCommand, HostApplicationReviewResponse>
{
    public async Task<Result<HostApplicationReviewResponse>> HandleAsync(ApproveHostApplicationCommand command, CancellationToken cancellationToken)
    {
        var id = new HostApplicationId(command.ApplicationId);
        var application = await applicationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (application is null)
            return Result<HostApplicationReviewResponse>.Failure(HostsErrors.ApplicationNotFound(id));

        if (application.Status == HostApplicationStatus.Submitted)
        {
            if (await hostRepository.GetByUserIdAsync(application.UserId, cancellationToken).ConfigureAwait(false) is not null)
                return Result<HostApplicationReviewResponse>.Failure(HostsErrors.AlreadyVerified);

            if (application.Cnpj is { } cnpj)
            {
                if (await hostRepository.ExistsByCnpjAsync(cnpj, cancellationToken).ConfigureAwait(false))
                    return Result<HostApplicationReviewResponse>.Failure(HostsErrors.CnpjAlreadyRegistered);
            }
            else if (await hostRepository.ExistsIndividualByCpfAsync(application.Cpf, cancellationToken).ConfigureAwait(false))
            {
                return Result<HostApplicationReviewResponse>.Failure(HostsErrors.CpfAlreadyRegistered);
            }
        }

        var now = timeProvider.GetUtcNow();
        var approved = application.Approve(command.AdminId, command.Reason, now);
        if (approved.IsFailure)
            return Result<HostApplicationReviewResponse>.FromFailure(approved);

        var host = approved.Value!;
        hostRepository.Add(host);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostApplicationReviewResponse>.FromFailure(saved);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostApplicationApproved,
                    AuditTargets.HostApplication,
                    application.Id.Value,
                    command.Reason,
                    new Dictionary<string, string?> { ["hostId"] = host.Id.Value.ToString() }),
                cancellationToken)
            .ConfigureAwait(false);

        await HostApplicationSupport
            .PurgeDocumentsIfDueAsync(application, policy, storage, unitOfWork, now, cancellationToken)
            .ConfigureAwait(false);

        var account = await HostApplicationSupport.GetAccountAsync(accounts, application.UserId, cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            await notifier
                .NotifyApprovedAsync(new HostApplicationDecision(account.Email, account.FullName), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostApplicationReviewResponse>.Success(HostApplicationReviewResponse.From(application, account));
    }
}
