namespace PetHost.Shared.Contracts.Audit;

/// <summary>
/// Contrato público do módulo Audit (§5): qualquer módulo registra por aqui o que
/// aconteceu, quem fez e sobre o quê, sem referenciar o Audit.
/// </summary>
/// <remarks>
/// Quem fez (<c>actor</c>), o IP e o trace id saem do request atual, a menos que o
/// registro diga outro ator — o login, por exemplo, ainda não tem token. O registro é
/// feito <b>depois</b> que a operação deu certo e nunca derruba o request: se gravar
/// falhar, o registro inteiro vai para o log como erro.
/// </remarks>
public interface IAuditTrail
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <summary>Um fato para a trilha de auditoria.</summary>
/// <param name="Action">O que aconteceu. Use as constantes de <see cref="AuditActions"/>.</param>
/// <param name="TargetType">Sobre que tipo de coisa. Use <see cref="AuditTargets"/>.</param>
/// <param name="TargetId">Sobre qual. Nulo quando não se sabe (login com e-mail inexistente).</param>
/// <param name="Reason">Motivo informado por quem fez — obrigatório nas ações do admin.</param>
/// <param name="Details">
/// Contexto curto, só texto. <b>Nunca</b> senha, token, CPF completo ou outro dado
/// sensível: CPF vai mascarado; campos alterados vão pelo nome, não pelo valor.
/// </param>
/// <param name="ActorId">Quem fez, quando não é o usuário do token (ou não há token).</param>
public sealed record AuditRecord(
    string Action,
    string TargetType,
    Guid? TargetId,
    string? Reason = null,
    IReadOnlyDictionary<string, string?>? Details = null,
    Guid? ActorId = null);

/// <summary>Tipos de alvo da trilha.</summary>
public static class AuditTargets
{
    /// <summary>Conta de usuário (módulo Auth). O id é o da conta.</summary>
    public const string Account = "account";

    /// <summary>Perfil de tutor (módulo Owners). O id é o do tutor.</summary>
    public const string Owner = "owner";

    /// <summary>Pet (módulo Pets). O id é o do pet.</summary>
    public const string Pet = "pet";

    /// <summary>Anfitrião verificado (módulo Hosts). O id é o do anfitrião.</summary>
    public const string Host = "host";

    /// <summary>Solicitação de cadastro de anfitrião (módulo Hosts). O id é o da solicitação.</summary>
    public const string HostApplication = "host_application";

    /// <summary>Cantinho (módulo Listings). O id é o do cantinho.</summary>
    public const string Listing = "listing";

    /// <summary>A própria trilha (consulta do admin).</summary>
    public const string Audit = "audit";
}

/// <summary>
/// Ações registradas, no formato <c>alvo.ação</c>. Contrato público: renomear quebra
/// filtros e relatórios já salvos.
/// </summary>
public static class AuditActions
{
    // --- Conta (Auth) ---
    public const string AccountRegistered = "account.registered";
    public const string AccountDeleted = "account.deleted";
    public const string AccountProfileUpdated = "account.profile_updated";
    public const string AccountDeactivated = "account.deactivated";
    public const string AccountReactivated = "account.reactivated";
    public const string AccountSuspended = "account.suspended";
    public const string AccountSuspensionLifted = "account.suspension_lifted";

    // --- Sessão e senha (Auth) ---
    public const string LoginSucceeded = "session.login_succeeded";
    public const string LoginFailed = "session.login_failed";
    public const string SessionSwitched = "session.switched";
    public const string PasswordChanged = "password.changed";
    public const string PasswordResetRequested = "password.reset_requested";
    public const string PasswordResetCompleted = "password.reset_completed";

    // --- Tutor (Owners) ---
    public const string OwnerRegistered = "owner.registered";
    public const string OwnerUpdated = "owner.updated";
    public const string OwnerDeactivated = "owner.deactivated";
    public const string OwnerReactivated = "owner.reactivated";
    public const string OwnerSuspended = "owner.suspended";
    public const string OwnerSuspensionLifted = "owner.suspension_lifted";
    public const string OwnerCpfReleased = "owner.cpf_released";

    /// <summary>Admin abriu os dados de um tutor (CPF completo): acesso a dado pessoal (LGPD).</summary>
    public const string OwnerViewed = "owner.viewed";

    /// <summary>Admin listou os tutores (CPF completo de todos).</summary>
    public const string OwnersListed = "owner.listed";

    // --- Pet (Pets) ---
    public const string PetRegistered = "pet.registered";
    public const string PetUpdated = "pet.updated";
    public const string PetDeactivated = "pet.deactivated";
    public const string PetReactivated = "pet.reactivated";

    // --- Anfitrião (Hosts) ---
    public const string HostUpdated = "host.updated";
    public const string HostDeactivated = "host.deactivated";
    public const string HostReactivated = "host.reactivated";
    public const string HostSuspended = "host.suspended";
    public const string HostSuspensionLifted = "host.suspension_lifted";
    public const string HostIdentityReleased = "host.identity_released";

    /// <summary>O anfitrião abriu o cadastro de recebimento: a conta conectada foi criada no Stripe.</summary>
    public const string HostStripeAccountCreated = "host.stripe_account_created";

    /// <summary>O Stripe liberou os repasses: os cantinhos do anfitrião entram na busca.</summary>
    public const string HostStripePayoutsEnabled = "host.stripe_payouts_enabled";

    /// <summary>O Stripe bloqueou os repasses: os cantinhos saem da busca até resolver.</summary>
    public const string HostStripePayoutsDisabled = "host.stripe_payouts_disabled";

    /// <summary>O Stripe recusou a conta conectada de vez (<c>rejected.*</c>): o admin decide o que fazer.</summary>
    public const string HostStripeAccountRejected = "host.stripe_account_rejected";

    /// <summary>Admin desligou a conta recusada pelo Stripe para o anfitrião tentar de novo. Com motivo.</summary>
    public const string HostStripeAccountReset = "host.stripe_account_reset";

    /// <summary>Lembrete enviado para o anfitrião terminar o cadastro de recebimento.</summary>
    public const string HostStripeReminderSent = "host.stripe_reminder_sent";

    /// <summary>Admin abriu os dados de um anfitrião (CPF/CNPJ completo): acesso a dado pessoal (LGPD).</summary>
    public const string HostViewed = "host.viewed";

    /// <summary>Admin listou os anfitriões.</summary>
    public const string HostsListed = "host.listed";

    // --- Solicitação de cadastro de anfitrião (Hosts) ---
    public const string HostApplicationStarted = "host_application.started";
    public const string HostApplicationUpdated = "host_application.updated";
    public const string HostApplicationDocumentUploaded = "host_application.document_uploaded";
    public const string HostApplicationSubmitted = "host_application.submitted";
    public const string HostApplicationCancelled = "host_application.cancelled";

    /// <summary>Admin aprovou: o perfil de anfitrião foi criado.</summary>
    public const string HostApplicationApproved = "host_application.approved";
    public const string HostApplicationRejected = "host_application.rejected";

    /// <summary>Admin abriu uma solicitação (CPF completo, dados da conta): acesso a dado pessoal (LGPD).</summary>
    public const string HostApplicationViewed = "host_application.viewed";

    /// <summary>Admin listou solicitações (CPF completo de todas).</summary>
    public const string HostApplicationsListed = "host_application.listed";

    /// <summary>Admin abriu uma foto de documento ou selfie: acesso a dado pessoal sensível (LGPD).</summary>
    public const string HostApplicationDocumentViewed = "host_application.document_viewed";

    /// <summary>Rascunho abandonado fechado pela tarefa de limpeza.</summary>
    public const string HostApplicationExpired = "host_application.expired";

    /// <summary>Fotos apagadas do bucket privado (prazo de guarda).</summary>
    public const string HostApplicationDocumentsPurged = "host_application.documents_purged";

    // --- Cantinho (Listings) ---
    public const string ListingCreated = "listing.created";
    public const string ListingUpdated = "listing.updated";
    public const string ListingDeactivated = "listing.deactivated";
    public const string ListingReactivated = "listing.reactivated";

    // --- Auditoria ---
    public const string AuditSearched = "audit.searched";
}
