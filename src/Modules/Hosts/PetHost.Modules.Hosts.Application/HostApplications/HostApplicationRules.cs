using FluentValidation;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Validation;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications;

/// <summary>
/// Regras de formato da solicitação, usadas pelos validadores. A regra mora no domínio
/// (<see cref="Cpf"/>, <c>...Values</c>); aqui só se pergunta a ele.
/// </summary>
internal static class HostApplicationRules
{
    public static IRuleBuilderOptions<T, string?> ValidCpf<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Must(cpf => Cpf.Create(cpf).IsSuccess).WithError(HostsErrors.CpfInvalid);

    public static IRuleBuilderOptions<T, string?> ValidDocumentType<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Must(type => IdentityDocumentTypeValues.TryParse(type, out _)).WithError(HostsErrors.DocumentTypeInvalid);

    public static IRuleBuilderOptions<T, string?> ValidDocumentKind<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule.Must(kind => DocumentKindValues.TryParse(kind, out _)).WithError(HostsErrors.DocumentKindInvalid);

    /// <summary>Toda decisão do admin leva motivo: ele vai para a nota e para a trilha.</summary>
    public static IRuleBuilderOptions<T, string?> AdminReason<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithError(HostsErrors.ReasonRequired)
            .Must(reason => reason!.Trim().Length <= HostApplication.ReviewNotesMaxLength).WithError(HostsErrors.ReasonTooLong);

    /// <summary>Veio algum dado de empresa no corpo?</summary>
    public static bool HasCompanyData(ICompanyFields fields) =>
        fields.Cnpj is not null || fields.LegalName is not null || fields.TradeName is not null || fields.CompanyAddress is not null;

    /// <summary>Os dados da empresa pela regra do domínio: todos os erros, com o campo do JSON.</summary>
    public static Result<CompanyDetails> ToCompany(ICompanyFields fields)
    {
        var address = fields.CompanyAddress;

        return CompanyDetails.Create(
            fields.Cnpj,
            fields.LegalName,
            fields.TradeName,
            address?.ZipCode,
            address?.Street,
            address?.Number,
            address?.Complement,
            address?.Neighborhood,
            address?.City,
            address?.State);
    }

    /// <summary>
    /// Os dados da empresa do PATCH: o que veio por cima do atual, campo a campo (inclusive no
    /// endereço), e o conjunto revalidado pela regra do domínio. Complemento vazio limpa.
    /// </summary>
    public static Result<CompanyDetails> MergeCompany(ICompanyFields fields, CompanyDetails current)
    {
        var address = fields.CompanyAddress;
        var currentAddress = current.Address;

        return CompanyDetails.Create(
            fields.Cnpj ?? current.Cnpj.Value,
            fields.LegalName ?? current.LegalName,
            fields.TradeName ?? current.TradeName,
            address?.ZipCode ?? currentAddress.ZipCode,
            address?.Street ?? currentAddress.Street,
            address?.Number ?? currentAddress.Number,
            address is null || address.Complement is null ? currentAddress.Complement : address.Complement,
            address?.Neighborhood ?? currentAddress.Neighborhood,
            address?.City ?? currentAddress.City,
            address?.State ?? currentAddress.State);
    }

    /// <summary>Acrescenta ao validador os erros dos dados da empresa (campos <c>cnpj</c>, <c>companyAddress.zipCode</c>...).</summary>
    public static void AddCompanyFailures<T>(ICompanyFields fields, ValidationContext<T> context)
    {
        var company = ToCompany(fields);
        if (company.IsFailure)
        {
            foreach (var error in company.Errors!)
                context.AddFailure(error);
        }
    }

    /// <summary>Valor já conferido pelo validador; aqui só converte.</summary>
    public static Cpf ToCpf(string? value) => Cpf.Create(value).Value!;

    public static IdentityDocumentType ToDocumentType(string? value) =>
        IdentityDocumentTypeValues.TryParse(value, out var type) ? type : throw NotValidated(nameof(value));

    public static DocumentKind ToDocumentKind(string? value) =>
        DocumentKindValues.TryParse(value, out var kind) ? kind : throw NotValidated(nameof(value));

    public static RejectionReason ToRejectionReason(string? value) =>
        RejectionReasonValues.TryParse(value, out var reason) ? reason : throw NotValidated(nameof(value));

    /// <summary>Chegou aqui sem passar pelo validador: bug, não entrada do usuário.</summary>
    private static ArgumentException NotValidated(string parameter) =>
        new("The value should have been validated before reaching the handler.", parameter);
}
