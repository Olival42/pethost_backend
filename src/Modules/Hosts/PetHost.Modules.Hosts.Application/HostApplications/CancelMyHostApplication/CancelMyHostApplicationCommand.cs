using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.CancelMyHostApplication;

/// <summary>A pessoa desiste da solicitação aberta (rascunho ou na fila).</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record CancelMyHostApplicationCommand(Guid UserId) : ICommand<HostApplicationResponse>;

public sealed class CancelMyHostApplicationCommandValidator : AbstractValidator<CancelMyHostApplicationCommand>
{
    public CancelMyHostApplicationCommandValidator() =>
        RuleFor(x => x.UserId).NotEmpty().WithError(HostsErrors.UserIdRequired);
}

/// <summary>
/// Fecha a solicitação e, conforme o prazo de guarda (padrão: na hora), apaga as fotos: quem
/// desiste não deixa documento para trás.
/// </summary>
public sealed class CancelMyHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IPrivateImageStorage storage,
    IHostVerificationPolicy policy,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CancelMyHostApplicationCommand, HostApplicationResponse>
{
    public async Task<Result<HostApplicationResponse>> HandleAsync(CancelMyHostApplicationCommand command, CancellationToken cancellationToken)
    {
        var open = await HostApplicationSupport.GetOpenAsync(applicationRepository, command.UserId, cancellationToken).ConfigureAwait(false);
        if (open.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(open);

        var application = open.Value!;
        var now = timeProvider.GetUtcNow();

        var cancelled = application.Cancel(now);
        if (cancelled.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(cancelled);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(saved);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostApplicationCancelled, AuditTargets.HostApplication, application.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        await HostApplicationSupport
            .PurgeDocumentsIfDueAsync(application, policy, storage, unitOfWork, now, cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationResponse>.Success(HostApplicationResponse.From(application));
    }
}
