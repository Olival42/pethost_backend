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

    // --- Auditoria ---
    public const string AuditSearched = "audit.searched";
}
