using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;

/// <summary>A conta de anfitrião logada abre a solicitação de cadastro (verificação de identidade).</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="PersonType"><c>pf</c> (padrão, pessoa física) ou <c>pj</c> (pessoa jurídica); maiúsculas também valem.</param>
/// <param name="Cpf">Pessoa física: o próprio CPF. Empresa: o CPF do representante legal (dono da conta).</param>
/// <param name="DocumentType"><c>rg</c>, <c>cnh</c> ou <c>cin</c> — do anfitrião ou do representante.</param>
/// <param name="AcceptNotice">A pessoa leu e aceitou o aviso de uso das fotos. Precisa ser <c>true</c>.</param>
/// <param name="IpAddress">IP do pedido (fica junto com o aceite do aviso). Vem do servidor, não do corpo.</param>
/// <param name="Cnpj">Só empresa.</param>
/// <param name="LegalName">Razão social. Só empresa.</param>
/// <param name="TradeName">Nome fantasia. Só empresa.</param>
/// <param name="CompanyAddress">Endereço da empresa. Só empresa.</param>
public sealed record StartHostApplicationCommand(
    Guid UserId,
    string? Cpf,
    string? DocumentType,
    bool? AcceptNotice,
    string? IpAddress,
    string? PersonType = null,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null) : ICommand<HostApplicationResponse>, ICompanyFields
{
    /// <summary>Pediu pessoa jurídica (o validador já conferiu o valor).</summary>
    public bool IsCompany => string.Equals(PersonType?.Trim(), PersonTypeValues.Company, StringComparison.OrdinalIgnoreCase);
}

public sealed class StartHostApplicationCommandValidator : AbstractValidator<StartHostApplicationCommand>
{
    public StartHostApplicationCommandValidator()
    {
        RuleFor(x => x.Cpf).ValidCpf();
        RuleFor(x => x.DocumentType).ValidDocumentType();
        RuleFor(x => x.AcceptNotice)
            .Equal(true).WithError(HostsErrors.NoticeNotAccepted);

        RuleFor(x => x.PersonType)
            .Must(type => type is null || type.Trim().ToLowerInvariant() is PersonTypeValues.Individual or PersonTypeValues.Company)
            .WithError(HostsErrors.PersonTypeInvalid);

        RuleFor(x => x).CustomErrors((command, context) =>
        {
            if (command.IsCompany)
                HostApplicationRules.AddCompanyFailures(command, context);
            else if (HostApplicationRules.HasCompanyData(command))
                context.AddFailure(HostsErrors.CompanyDataNotExpected);
        });
    }
}
