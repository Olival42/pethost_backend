using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Hosts.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Hosts. Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class HostsErrors
{
    // --- Validação de formato (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error CpfInvalid =
        Error.Validation("cpf", "CPF is invalid.");

    public static readonly Error CnpjInvalid =
        Error.Validation("cnpj", "CNPJ is invalid.");

    public static readonly Error LegalNameRequired =
        Error.Validation("legalName", "Legal name is required for a company.");

    public static readonly Error LegalNameTooLong =
        Error.Validation("legalName", $"Legal name must be at most {Host.LegalNameMaxLength} characters.");

    public static readonly Error TradeNameRequired =
        Error.Validation("tradeName", "Trade name is required for a company.");

    public static readonly Error TradeNameTooLong =
        Error.Validation("tradeName", $"Trade name must be at most {Host.TradeNameMaxLength} characters.");

    // --- Endereço da empresa (o campo leva o prefixo "companyAddress." porque o JSON é aninhado) ---

    public static readonly Error CompanyZipCodeInvalid =
        Error.Validation("companyAddress.zipCode", $"Zip code (CEP) must have {CompanyAddress.ZipCodeLength} digits.");

    public static readonly Error CompanyStreetRequired =
        Error.Validation("companyAddress.street", "Street is required.");

    public static readonly Error CompanyStreetTooLong =
        Error.Validation("companyAddress.street", $"Street must be at most {CompanyAddress.StreetMaxLength} characters.");

    public static readonly Error CompanyStreetNumberRequired =
        Error.Validation("companyAddress.number", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error CompanyStreetNumberTooLong =
        Error.Validation("companyAddress.number", $"Number must be at most {CompanyAddress.NumberMaxLength} characters.");

    public static readonly Error CompanyComplementTooLong =
        Error.Validation("companyAddress.complement", $"Complement must be at most {CompanyAddress.ComplementMaxLength} characters.");

    public static readonly Error CompanyNeighborhoodRequired =
        Error.Validation("companyAddress.neighborhood", "Neighborhood is required.");

    public static readonly Error CompanyNeighborhoodTooLong =
        Error.Validation("companyAddress.neighborhood", $"Neighborhood must be at most {CompanyAddress.NeighborhoodMaxLength} characters.");

    public static readonly Error CompanyCityRequired =
        Error.Validation("companyAddress.city", "City is required.");

    public static readonly Error CompanyCityTooLong =
        Error.Validation("companyAddress.city", $"City must be at most {CompanyAddress.CityMaxLength} characters.");

    public static readonly Error CompanyStateInvalid =
        Error.Validation("companyAddress.state", "State must be a valid Brazilian state code, e.g. 'PR'.");

    // --- Solicitação de cadastro: validação de formato ---

    public static readonly Error DocumentTypeInvalid =
        Error.Validation("documentType", $"Document type must be one of: {string.Join(", ", IdentityDocumentTypeValues.All)}.");

    public static readonly Error DocumentKindInvalid =
        Error.Validation("kind", $"Document kind must be one of: {string.Join(", ", DocumentKindValues.All)}.");

    /// <summary>O envio das fotos veio sem nenhuma das partes <c>front</c>, <c>back</c> ou <c>selfie</c>.</summary>
    public static readonly Error DocumentsRequired =
        Error.Validation("documents", $"Send at least one photo in a multipart/form-data request, in the fields: {string.Join(", ", DocumentKindValues.All)}.");

    /// <summary>Sem ler e aceitar o aviso de uso das fotos, a verificação não começa.</summary>
    public static readonly Error NoticeNotAccepted =
        Error.Validation("acceptNotice", "You must read and accept how the photos are used before starting the verification.");

    public static readonly Error RejectionReasonInvalid =
        Error.Validation("rejectionReason", $"Rejection reason must be one of: {string.Join(", ", RejectionReasonValues.All)}.");

    public static readonly Error ReasonRequired =
        Error.Validation("reason", "A reason is required.");

    public static readonly Error ReasonTooLong =
        Error.Validation("reason", $"Reason must be at most {HostApplication.ReviewNotesMaxLength} characters.");

    public static readonly Error StatusFilterInvalid =
        Error.Validation("status", $"Status must be one of: {string.Join(", ", HostApplicationStatusValues.All)}.");

    public static readonly Error PersonTypeInvalid =
        Error.Validation("personType", $"Person type must be one of: {PersonTypeValues.Individual}, {PersonTypeValues.Company}.");

    /// <summary>Pessoa física não manda dados de empresa (o campo indica onde está o erro).</summary>
    public static readonly Error CompanyDataNotExpected =
        Error.Validation("cnpj", "Company data is only accepted when personType is 'company'.");

    // --- Solicitação de cadastro: regras de negócio ---

    /// <summary>422: dados de empresa numa solicitação aberta como pessoa física.</summary>
    public static readonly Error CompanyDataNotAllowed =
        new("HOST_APPLICATION_COMPANY_DATA_NOT_ALLOWED", "Company data is only accepted in a company host application.");

    /// <summary>409: o CNPJ já é de um anfitrião ou de uma solicitação aberta de outra conta.</summary>
    public static readonly Error CnpjAlreadyRegistered =
        new("HOST_CNPJ_ALREADY_REGISTERED", "A host or an open host application with this CNPJ already exists. If this company is yours, contact support.");

    /// <summary>422: a conta já é anfitriã verificada.</summary>
    public static readonly Error AlreadyVerified =
        new("HOST_ALREADY_VERIFIED", "This account is already a verified host.");

    /// <summary>409: já existe uma solicitação aberta (rascunho ou na fila).</summary>
    public static readonly Error ApplicationAlreadyOpen =
        new("HOST_APPLICATION_ALREADY_EXISTS", "This account already has an open host application.");

    /// <summary>422: houve recusa por suspeita de fraude; nova tentativa só pelo suporte.</summary>
    public static readonly Error ApplicationBlocked =
        new("HOST_APPLICATION_BLOCKED", "A new host application cannot be started for this account. Contact support.");

    /// <summary>422: as tentativas acabaram.</summary>
    public static readonly Error AttemptsExceeded =
        new("HOST_APPLICATION_ATTEMPTS_EXCEEDED", $"The limit of {HostApplication.MaxAttempts} host applications was reached. Contact support.");

    /// <summary>409: o CPF já é de um anfitrião ou de uma solicitação aberta de outra conta.</summary>
    public static readonly Error CpfAlreadyRegistered =
        new("HOST_CPF_ALREADY_REGISTERED", "A host or an open host application with this CPF already exists. If this CPF is yours, contact support.");

    /// <summary>422: só o rascunho muda (dados e fotos).</summary>
    public static readonly Error ApplicationNotEditable =
        new("HOST_APPLICATION_NOT_EDITABLE", "This host application can no longer be changed.");

    /// <summary>422: faltam fotos para enviar.</summary>
    public static Error DocumentsMissing(IEnumerable<DocumentKind> missing) =>
        new("HOST_APPLICATION_DOCUMENTS_MISSING", $"Send all the photos before submitting. Missing: {string.Join(", ", missing.Select(DocumentKindValues.ToWire))}.");

    /// <summary>409: a mesma imagem já foi enviada em outra vaga (frente igual ao verso).</summary>
    public static readonly Error DocumentAlreadyUsed =
        new("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS", "This image was already sent as another photo of this application.");

    /// <summary>422: o admin só decide solicitação que está na fila.</summary>
    public static readonly Error ApplicationNotUnderReview =
        new("HOST_APPLICATION_NOT_UNDER_REVIEW", "Only a submitted host application can be approved or rejected.");

    /// <summary>422: a solicitação já fechou.</summary>
    public static readonly Error ApplicationClosed =
        new("HOST_APPLICATION_CLOSED", "This host application is already closed.");

    /// <summary>409: outra pessoa mexeu na solicitação ao mesmo tempo (dois admins).</summary>
    public static readonly Error ApplicationConflict =
        new("HOST_APPLICATION_CONFLICT", "This host application was changed at the same time by someone else. Reload it and try again.");

    /// <summary>404: a conta do token não tem solicitação aberta.</summary>
    public static readonly Error OpenApplicationNotFound =
        new("HOST_APPLICATION_NOT_FOUND", "This account has no open host application.");

    /// <summary>404: não existe solicitação com esse id.</summary>
    public static Error ApplicationNotFound(HostApplicationId id) =>
        new("HOST_APPLICATION_NOT_FOUND", $"Host application '{id.Value}' was not found.");

    /// <summary>404: a foto não existe (nunca enviada ou já apagada pelo prazo de guarda).</summary>
    public static readonly Error DocumentNotFound =
        new("HOST_APPLICATION_DOCUMENT_NOT_FOUND", "This photo is not available. It was never sent or was already deleted.");

    // --- Anfitrião verificado ---

    /// <summary>
    /// 422: nome, nascimento, CPF, CNPJ e razão social foram conferidos na verificação; mudar
    /// exige uma nova verificação (pelo suporte).
    /// </summary>
    public static readonly Error IdentityLocked =
        new("HOST_IDENTITY_LOCKED", "Name, birth date, CPF, CNPJ and legal name were verified and cannot be changed here. Contact support for a new verification.");

    /// <summary>422: nome fantasia e endereço da empresa são só de anfitrião pessoa jurídica.</summary>
    public static readonly Error NotACompany =
        new("HOST_NOT_A_COMPANY", "Company data can only be changed by a company host.");

    /// <summary>422: liberar CPF/CNPJ só de anfitrião suspenso.</summary>
    public static readonly Error SuspensionRequired =
        new("HOST_SUSPENSION_REQUIRED", "The CPF/CNPJ can only be released from a suspended host.");

    /// <summary>422: o CPF/CNPJ deste anfitrião foi liberado; a suspensão não sai mais.</summary>
    public static readonly Error IdentityReleased =
        new("HOST_IDENTITY_RELEASED", "This host's CPF/CNPJ was released, so the suspension cannot be lifted.");

    /// <summary>404: não existe anfitrião com esse id.</summary>
    public static Error NotFound(HostId id) =>
        new("HOST_NOT_FOUND", $"Host '{id.Value}' was not found.");

    /// <summary>404: a conta do token ainda não é anfitriã verificada.</summary>
    public static readonly Error ProfileNotFound =
        new("HOST_NOT_FOUND", "This account is not a verified host yet.");
}
