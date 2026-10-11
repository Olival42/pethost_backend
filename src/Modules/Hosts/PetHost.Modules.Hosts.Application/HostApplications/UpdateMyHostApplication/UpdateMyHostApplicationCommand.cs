using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.HostApplications.UpdateMyHostApplication;

/// <summary>
/// Corrige o rascunho: CPF, tipo de documento e/ou os dados da empresa. Só o que vier muda —
/// inclusive na empresa: o que vier (CNPJ, razão social, nome fantasia, campos do endereço) é
/// completado com o rascunho e o conjunto é conferido no handler.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record UpdateMyHostApplicationCommand(
    Guid UserId,
    string? Cpf,
    string? DocumentType,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null) : ICommand<HostApplicationResponse>, ICompanyFields;

public sealed class UpdateMyHostApplicationCommandValidator : AbstractValidator<UpdateMyHostApplicationCommand>
{
    public UpdateMyHostApplicationCommandValidator()
    {
        RuleFor(x => x.Cpf).ValidCpf().When(x => x.Cpf is not null);
        RuleFor(x => x.DocumentType).ValidDocumentType().When(x => x.DocumentType is not null);

        // Dados da empresa: parciais, completados com o rascunho e conferidos no handler.
    }
}
