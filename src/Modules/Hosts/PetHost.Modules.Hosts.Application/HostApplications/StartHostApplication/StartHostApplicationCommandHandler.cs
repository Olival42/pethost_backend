using System.Globalization;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;

/// <summary>
/// Abre a solicitação em rascunho (pessoa física ou jurídica). As regras de "pode abrir?"
/// (já aprovada, já aberta, bloqueada, tentativas) moram no agregado; aqui se confere o CPF
/// (é desta pessoa?) e o CNPJ (está livre?) contra os outros.
/// </summary>
public sealed class StartHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    ICpfOwnership cpfOwnership,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<StartHostApplicationCommand, HostApplicationResponse>
{
    public async Task<Result<HostApplicationResponse>> HandleAsync(StartHostApplicationCommand command, CancellationToken cancellationToken)
    {
        if (await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false) is not null)
            return Result<HostApplicationResponse>.Failure(HostsErrors.AlreadyVerified);

        var history = await applicationRepository.ListByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        var cpf = HostApplicationRules.ToCpf(command.Cpf);
        var company = command.IsCompany ? HostApplicationRules.ToCompany(command).Value : null;

        var started = HostApplication.Start(
            history,
            command.UserId,
            cpf,
            HostApplicationRules.ToDocumentType(command.DocumentType),
            company,
            HostVerificationNotice.CurrentVersion,
            command.IpAddress,
            timeProvider.GetUtcNow());

        if (started.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(started);

        var cpfAvailable = await HostApplicationSupport
            .EnsureCpfAvailableAsync(cpf, command.UserId, cpfOwnership, cancellationToken)
            .ConfigureAwait(false);
        if (cpfAvailable.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(cpfAvailable);

        if (company is not null)
        {
            var cnpjAvailable = await HostApplicationSupport
                .EnsureCnpjAvailableAsync(company.Cnpj, command.UserId, hostRepository, applicationRepository, cancellationToken)
                .ConfigureAwait(false);
            if (cnpjAvailable.IsFailure)
                return Result<HostApplicationResponse>.FromFailure(cnpjAvailable);
        }

        var application = started.Value!;
        applicationRepository.Add(application);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(saved);

        var details = new Dictionary<string, string?>
        {
            ["personType"] = PersonTypeValues.ToWire(application.PersonType),
            ["cpf"] = cpf.ToString(),
            ["attempt"] = application.AttemptNumber.ToString(CultureInfo.InvariantCulture),
            ["noticeVersion"] = application.NoticeVersion,
        };
        if (company is not null)
            details["cnpj"] = company.Cnpj.Value;

        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.HostApplicationStarted, AuditTargets.HostApplication, application.Id.Value, Details: details), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostApplicationResponse>.Success(HostApplicationResponse.From(application));
    }
}
