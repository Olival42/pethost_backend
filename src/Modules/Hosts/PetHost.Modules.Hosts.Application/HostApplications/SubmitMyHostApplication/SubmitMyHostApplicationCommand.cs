using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.SubmitMyHostApplication;

/// <summary>Envia o rascunho para a fila do admin. Precisa das três fotos.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record SubmitMyHostApplicationCommand(Guid UserId) : ICommand<HostApplicationResponse>;

public sealed class SubmitMyHostApplicationCommandValidator : AbstractValidator<SubmitMyHostApplicationCommand>
{
    public SubmitMyHostApplicationCommandValidator() =>
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

public sealed class SubmitMyHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostsUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SubmitMyHostApplicationCommand, HostApplicationResponse>
{
    public async Task<Result<HostApplicationResponse>> HandleAsync(SubmitMyHostApplicationCommand command, CancellationToken cancellationToken)
    {
        var open = await HostApplicationSupport.GetOpenAsync(applicationRepository, command.UserId, cancellationToken).ConfigureAwait(false);
        if (open.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(open);

        var application = open.Value!;
        var submitted = application.Submit(timeProvider.GetUtcNow());
        if (submitted.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(submitted);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(saved);

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostApplicationSubmitted, AuditTargets.HostApplication, application.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationResponse>.Success(HostApplicationResponse.From(application));
    }
}
