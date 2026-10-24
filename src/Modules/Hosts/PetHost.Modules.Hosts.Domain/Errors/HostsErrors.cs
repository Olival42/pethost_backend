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
        Error.Validation("cpf", "HOST_CPF_INVALID", "CPF is invalid.");

    public static readonly Error CnpjInvalid =
        Error.Validation("cnpj", "HOST_CNPJ_INVALID", "CNPJ is invalid.");

    public static readonly Error LegalNameRequired =
        Error.Validation("legalName", "HOST_LEGAL_NAME_REQUIRED", "Legal name is required for a company.");

    public static readonly Error LegalNameTooLong =
        Error.Validation("legalName", "HOST_LEGAL_NAME_TOO_LONG", $"Legal name must be at most {Host.LegalNameMaxLength} characters.", ("max", Host.LegalNameMaxLength));

    public static readonly Error TradeNameRequired =
        Error.Validation("tradeName", "HOST_TRADE_NAME_REQUIRED", "Trade name is required for a company.");

    public static readonly Error TradeNameTooLong =
        Error.Validation("tradeName", "HOST_TRADE_NAME_TOO_LONG", $"Trade name must be at most {Host.TradeNameMaxLength} characters.", ("max", Host.TradeNameMaxLength));

    // --- Endereço da empresa (o campo leva o prefixo "companyAddress." porque o JSON é aninhado) ---

    public static readonly Error CompanyZipCodeInvalid =
        Error.Validation("companyAddress.zipCode", "HOST_COMPANY_ZIP_CODE_INVALID", $"Zip code (CEP) must have {CompanyAddress.ZipCodeLength} digits.", ("digits", CompanyAddress.ZipCodeLength));

    public static readonly Error CompanyStreetRequired =
        Error.Validation("companyAddress.street", "HOST_COMPANY_STREET_REQUIRED", "Street is required.");

    public static readonly Error CompanyStreetTooLong =
        Error.Validation("companyAddress.street", "HOST_COMPANY_STREET_TOO_LONG", $"Street must be at most {CompanyAddress.StreetMaxLength} characters.", ("max", CompanyAddress.StreetMaxLength));

    public static readonly Error CompanyStreetNumberRequired =
        Error.Validation("companyAddress.number", "HOST_COMPANY_STREET_NUMBER_REQUIRED", "Number is required. Use 'S/N' when there is none.");

    public static readonly Error CompanyStreetNumberTooLong =
        Error.Validation("companyAddress.number", "HOST_COMPANY_STREET_NUMBER_TOO_LONG", $"Number must be at most {CompanyAddress.NumberMaxLength} characters.", ("max", CompanyAddress.NumberMaxLength));

    public static readonly Error CompanyComplementTooLong =
        Error.Validation("companyAddress.complement", "HOST_COMPANY_COMPLEMENT_TOO_LONG", $"Complement must be at most {CompanyAddress.ComplementMaxLength} characters.", ("max", CompanyAddress.ComplementMaxLength));

    public static readonly Error CompanyNeighborhoodRequired =
        Error.Validation("companyAddress.neighborhood", "HOST_COMPANY_NEIGHBORHOOD_REQUIRED", "Neighborhood is required.");

    public static readonly Error CompanyNeighborhoodTooLong =
        Error.Validation("companyAddress.neighborhood", "HOST_COMPANY_NEIGHBORHOOD_TOO_LONG", $"Neighborhood must be at most {CompanyAddress.NeighborhoodMaxLength} characters.", ("max", CompanyAddress.NeighborhoodMaxLength));

    public static readonly Error CompanyCityRequired =
        Error.Validation("companyAddress.city", "HOST_COMPANY_CITY_REQUIRED", "City is required.");

    public static readonly Error CompanyCityTooLong =
        Error.Validation("companyAddress.city", "HOST_COMPANY_CITY_TOO_LONG", $"City must be at most {CompanyAddress.CityMaxLength} characters.", ("max", CompanyAddress.CityMaxLength));

    public static readonly Error CompanyStateInvalid =
        Error.Validation("companyAddress.state", "HOST_COMPANY_STATE_INVALID", "State must be a valid Brazilian state code, e.g. 'PR'.");

    // --- Solicitação de cadastro: validação de formato ---

    public static readonly Error DocumentTypeInvalid =
        Error.Validation("documentType", "HOST_DOCUMENT_TYPE_INVALID", $"Document type must be one of: {string.Join(", ", IdentityDocumentTypeValues.All)}.");

    public static readonly Error DocumentKindInvalid =
        Error.Validation("kind", "HOST_DOCUMENT_KIND_INVALID", $"Document kind must be one of: {string.Join(", ", DocumentKindValues.All)}.");

    /// <summary>O envio das fotos veio sem nenhuma das partes <c>front</c>, <c>back</c> ou <c>selfie</c>.</summary>
    public static readonly Error DocumentsRequired =
        Error.Validation("documents", "HOST_DOCUMENTS_REQUIRED", $"Send at least one photo in a multipart/form-data request, in the fields: {string.Join(", ", DocumentKindValues.All)}.");

    /// <summary>Sem ler e aceitar o aviso de uso das fotos, a verificação não começa.</summary>
    public static readonly Error NoticeNotAccepted =
        Error.Validation("acceptNotice", "HOST_NOTICE_NOT_ACCEPTED", "You must read and accept how the photos are used before starting the verification.");

    public static readonly Error RejectionReasonInvalid =
        Error.Validation("rejectionReason", "HOST_REJECTION_REASON_INVALID", $"Rejection reason must be one of: {string.Join(", ", RejectionReasonValues.All)}.");

    public static readonly Error ReasonRequired =
        Error.Validation("reason", "HOST_REASON_REQUIRED", "A reason is required.");

    public static readonly Error ReasonTooLong =
        Error.Validation("reason", "HOST_REASON_TOO_LONG", $"Reason must be at most {HostApplication.ReviewNotesMaxLength} characters.", ("max", HostApplication.ReviewNotesMaxLength));

    public static readonly Error StatusFilterInvalid =
        Error.Validation("status", "HOST_STATUS_FILTER_INVALID", $"Status must be one of: {string.Join(", ", HostApplicationStatusValues.All)}.");

    public static readonly Error StripeStatusFilterInvalid =
        Error.Validation("stripeStatus", "HOST_STRIPE_STATUS_FILTER_INVALID", $"Stripe status must be one of: {string.Join(", ", StripeOnboardingStatusValues.All)}.");

    public static readonly Error DashboardMonthInvalid =
        Error.Validation("month", "HOST_DASHBOARD_MONTH_INVALID", "Month must be in the format yyyy-MM (e.g. 2026-10).");

    public static readonly Error DashboardMonthInFuture =
        Error.Validation("month", "HOST_DASHBOARD_MONTH_IN_FUTURE", "Month cannot be in the future.");

    public static readonly Error PersonTypeInvalid =
        Error.Validation("personType", "HOST_PERSON_TYPE_INVALID", $"Person type must be one of: {PersonTypeValues.Individual}, {PersonTypeValues.Company}.");

    /// <summary>Pessoa física não manda dados de empresa (o campo indica onde está o erro).</summary>
    public static readonly Error CompanyDataNotExpected =
        Error.Validation("cnpj", "HOST_COMPANY_DATA_NOT_EXPECTED", "Company data is only accepted when personType is 'company'.");

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

    /// <summary>
    /// 422: o CPF está bloqueado para solicitação de anfitrião (recusa por fraude ou tentativas
    /// esgotadas, em qualquer conta, mesmo excluída). Só o suporte libera.
    /// </summary>
    public static readonly Error IdentityBlocked =
        new("HOST_IDENTITY_BLOCKED", "A host application cannot be started with this CPF. Contact support.");

    /// <summary>404: o CPF informado pelo admin não está bloqueado.</summary>
    public static readonly Error IdentityBlockNotFound =
        new("HOST_IDENTITY_BLOCK_NOT_FOUND", "This CPF is not blocked.");

    /// <summary>422: as tentativas acabaram.</summary>
    public static readonly Error AttemptsExceeded =
        new Error("HOST_APPLICATION_ATTEMPTS_EXCEEDED", $"The limit of {HostApplication.MaxAttempts} host applications was reached. Contact support.")
            .WithParams(("max", HostApplication.MaxAttempts));

    /// <summary>409: o CPF já é de um anfitrião ou de uma solicitação aberta de outra conta.</summary>
    public static readonly Error CpfAlreadyRegistered =
        new("HOST_CPF_ALREADY_REGISTERED", "A host or an open host application with this CPF already exists. If this CPF is yours, contact support.");

    /// <summary>422: só o rascunho muda (dados e fotos).</summary>
    public static readonly Error ApplicationNotEditable =
        new("HOST_APPLICATION_NOT_EDITABLE", "This host application can no longer be changed.");

    /// <summary>422: faltam fotos para enviar.</summary>
    public static Error DocumentsMissing(IEnumerable<DocumentKind> missing) =>
        new Error("HOST_APPLICATION_DOCUMENTS_MISSING", $"Send all the photos before submitting. Missing: {string.Join(", ", missing.Select(DocumentKindValues.ToWire))}.")
            .WithParams(("missing", string.Join(",", missing.Select(DocumentKindValues.ToWire))));

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

    // --- Cadastro de recebimento (Stripe Connect) ---

    /// <summary>422: anfitrião inativo ou suspenso não abre nem mexe no cadastro de recebimento.</summary>
    public static readonly Error StripeOnboardingNotAllowed =
        new("HOST_STRIPE_ONBOARDING_NOT_ALLOWED", "The payout setup is only available to an active host that is not suspended.");

    /// <summary>422: o Stripe recusou a conta de vez; só o suporte (admin) libera uma nova tentativa.</summary>
    public static readonly Error StripeAccountRejected =
        new("HOST_STRIPE_ACCOUNT_REJECTED", "Stripe did not approve your payout account. Contact support to continue.");

    /// <summary>422: desligar a conta conectada só vale para conta recusada pelo Stripe.</summary>
    public static readonly Error StripeAccountNotRejected =
        new("HOST_STRIPE_ACCOUNT_NOT_REJECTED", "Only a Stripe account rejected by Stripe can be reset.");

    /// <summary>404: o anfitrião ainda não abriu o cadastro de recebimento (não há conta conectada).</summary>
    public static readonly Error StripeAccountNotFound =
        new("HOST_STRIPE_ACCOUNT_NOT_FOUND", "This host has not started the payout setup yet.");

    /// <summary>409: o anfitrião já tem outra conta conectada (ou a conta já é de outro anfitrião).</summary>
    public static readonly Error StripeAccountAlreadyLinked =
        new("HOST_STRIPE_ACCOUNT_ALREADY_EXISTS", "This host is already linked to another Stripe account. Contact support to change it.");

    /// <summary>422: o Stripe recusou os dados enviados (ex.: CPF ou endereço que ele não aceita).</summary>
    public static readonly Error StripeRequestRejected =
        new("HOST_STRIPE_REQUEST_REJECTED", "Stripe did not accept the payout setup with the data on this account. Check your profile data or contact support.");

    /// <summary>503: o Stripe está fora do ar, lento ou não configurado neste servidor. Tente de novo depois.</summary>
    public static readonly Error StripeUnavailable =
        new("HOST_STRIPE_UNAVAILABLE", "The payment provider is unavailable right now. Try again in a few minutes.");

    /// <summary>404: não existe anfitrião com esse id.</summary>
    public static Error NotFound(HostId id) =>
        new("HOST_NOT_FOUND", $"Host '{id.Value}' was not found.");

    /// <summary>404: a conta do token ainda não é anfitriã verificada.</summary>
    public static readonly Error ProfileNotFound =
        new("HOST_NOT_FOUND", "This account is not a verified host yet.");

    /// <summary>
    /// 422: o anfitrião tem reserva em aberto (solicitada, aceita, confirmada ou em andamento).
    /// Responda, cancele ou conclua as estadias antes de inativar o perfil.
    /// </summary>
    public static readonly Error HasOpenBooking =
        new("HOST_HAS_OPEN_BOOKING", "This host has open bookings. Answer, cancel or finish them before deactivating or deleting the profile.");

    /// <summary>400: excluir a conta pede a senha atual.</summary>
    public static readonly Error DeletionPasswordRequired =
        Error.Validation("currentPassword", "HOST_DELETION_PASSWORD_REQUIRED", "Current password is required to delete the account.");

    /// <summary>400: a senha atual não confere.</summary>
    public static readonly Error CurrentPasswordIncorrect =
        Error.Validation("currentPassword", "HOST_CURRENT_PASSWORD_INCORRECT", "Current password is incorrect.");

    // --- Validação: identificadores e campos obrigatórios de comandos internos e de rota ---

    public static readonly Error UserIdRequired =
        Error.Validation("userId", "HOST_USER_ID_REQUIRED", "User id is required.");

    public static readonly Error EmailRequired =
        Error.Validation("email", "HOST_EMAIL_REQUIRED", "Email is required.");

    public static readonly Error PasswordRequired =
        Error.Validation("password", "HOST_PASSWORD_REQUIRED", "Password is required.");

    public static readonly Error HostIdRequired =
        Error.Validation("hostId", "HOST_HOST_ID_REQUIRED", "Host id is required.");

    public static readonly Error StripeAccountIdRequired =
        Error.Validation("stripeAccountId", "HOST_STRIPE_ACCOUNT_ID_REQUIRED", "Stripe account id is required.");
}
