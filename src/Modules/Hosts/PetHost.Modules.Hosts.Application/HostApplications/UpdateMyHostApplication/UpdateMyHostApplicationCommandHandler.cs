using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.UpdateMyHostApplication;

/// <summary>
/// Corrige os dados do rascunho. CPF novo (é desta pessoa?) e CNPJ novo (está livre?) são
/// conferidos antes de gravar.
/// </summary>
public sealed class UpdateMyHostApplicationCommandHandler(
    IHostApplicationRepository applicationRepository,
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    ICpfOwnership cpfOwnership,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdateMyHostApplicationCommand, HostApplicationResponse>
{
    public async Task<Result<HostApplicationResponse>> HandleAsync(UpdateMyHostApplicationCommand command, CancellationToken cancellationToken)
    {
        var open = await HostApplicationSupport.GetOpenAsync(applicationRepository, command.UserId, cancellationToken).ConfigureAwait(false);
        if (open.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(open);

        var application = open.Value!;
        var cpf = command.Cpf is null ? null : HostApplicationRules.ToCpf(command.Cpf);
        var documentType = command.DocumentType is null ? (IdentityDocumentType?)null : HostApplicationRules.ToDocumentType(command.DocumentType);

        // Recusas de estado (já enviada, empresa em pessoa física) vêm antes das conferências.
        var accepts = application.EnsureAcceptsDocuments();
        if (accepts.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(accepts);

        // Empresa: o que veio por cima do rascunho, e o conjunto revalidado.
        CompanyDetails? company = null;
        if (HostApplicationRules.HasCompanyData(command))
        {
            if (application.Company is not { } current)
                return Result<HostApplicationResponse>.Failure(HostsErrors.CompanyDataNotAllowed);

            var merged = HostApplicationRules.MergeCompany(command, current);
            if (merged.IsFailure)
                return Result<HostApplicationResponse>.FromFailure(merged);

            company = merged.Value;
        }

        var changedFields = new List<string>();
        if (cpf is not null && cpf != application.Cpf)
            changedFields.Add("cpf");
        if (documentType is { } type && type != application.DocumentType)
            changedFields.Add("documentType");
        if (company is not null && company != application.Company)
            changedFields.Add("company");

        if (changedFields.Contains("cpf"))
        {
            var available = await HostApplicationSupport
                .EnsureCpfAvailableAsync(cpf!, command.UserId, cpfOwnership, cancellationToken)
                .ConfigureAwait(false);
            if (available.IsFailure)
                return Result<HostApplicationResponse>.FromFailure(available);
        }

        if (company is not null && company.Cnpj != application.Cnpj)
        {
            var available = await HostApplicationSupport
                .EnsureCnpjAvailableAsync(company.Cnpj, command.UserId, hostRepository, applicationRepository, cancellationToken)
                .ConfigureAwait(false);
            if (available.IsFailure)
                return Result<HostApplicationResponse>.FromFailure(available);
        }

        var changed = application.ChangeDetails(cpf, documentType, company, timeProvider.GetUtcNow());
        if (changed.IsFailure)
            return Result<HostApplicationResponse>.FromFailure(changed);

        if (changedFields.Count > 0)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<HostApplicationResponse>.FromFailure(saved);

            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.HostApplicationUpdated,
                        AuditTargets.HostApplication,
                        application.Id.Value,
                        Details: new Dictionary<string, string?> { ["fields"] = string.Join(",", changedFields) }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostApplicationResponse>.Success(HostApplicationResponse.From(application));
    }
}
