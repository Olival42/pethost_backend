using PetHost.Shared.Kernel.Domain;

namespace PetHost.Modules.Audit.Domain.Entries;

/// <summary>
/// Um fato da trilha de auditoria: quem fez o quê, sobre o quê, quando e de onde.
/// Espelha a tabela <c>audit.audit_entries</c>. <b>Imutável</b>: uma vez gravado, nunca
/// é alterado nem apagado pela aplicação.
/// </summary>
public sealed class AuditEntry : Entity<AuditEntryId>
{
    public const int ActionMaxLength = 64;
    public const int TargetTypeMaxLength = 32;
    public const int ActorRoleMaxLength = 16;
    public const int ReasonMaxLength = 500;
    public const int IpAddressMaxLength = 45;
    public const int TraceIdMaxLength = 64;
    public const int DetailKeyMaxLength = 64;
    public const int DetailValueMaxLength = 256;
    public const int MaxDetails = 20;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private AuditEntry()
    {
        Action = null!;
        TargetType = null!;
        Details = null!;
    }

    private AuditEntry(
        DateTimeOffset occurredAt,
        string action,
        string targetType,
        Guid? targetId,
        Guid? actorId,
        string? actorRole,
        string? reason,
        Dictionary<string, string?> details,
        string? ipAddress,
        string? traceId)
        : base(AuditEntryId.New())
    {
        OccurredAt = occurredAt;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        ActorId = actorId;
        ActorRole = actorRole;
        Reason = reason;
        Details = details;
        IpAddress = ipAddress;
        TraceId = traceId;
    }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>O que aconteceu, no formato <c>alvo.ação</c> (<c>owner.suspended</c>).</summary>
    public string Action { get; private set; }

    public string TargetType { get; private set; }

    public Guid? TargetId { get; private set; }

    /// <summary>Quem fez. Nulo em ação anônima sem conta conhecida (login com e-mail inexistente).</summary>
    public Guid? ActorId { get; private set; }

    public string? ActorRole { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Contexto curto em texto. Nunca dado sensível (senha, token, CPF completo).</summary>
    public Dictionary<string, string?> Details { get; private set; }

    public string? IpAddress { get; private set; }

    public string? TraceId { get; private set; }

    /// <summary>
    /// Monta o registro. Textos longos demais são cortados em vez de recusados: a trilha
    /// não pode perder um fato por causa do tamanho de um campo.
    /// </summary>
    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        string action,
        string targetType,
        Guid? targetId,
        Guid? actorId,
        string? actorRole,
        string? reason,
        IReadOnlyDictionary<string, string?>? details,
        string? ipAddress,
        string? traceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        var cleanDetails = (details ?? new Dictionary<string, string?>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Key))
            .Take(MaxDetails)
            .ToDictionary(
                d => Cut(d.Key.Trim(), DetailKeyMaxLength)!,
                d => Cut(d.Value, DetailValueMaxLength),
                StringComparer.Ordinal);

        return new AuditEntry(
            occurredAt,
            Cut(action.Trim(), ActionMaxLength)!,
            Cut(targetType.Trim(), TargetTypeMaxLength)!,
            targetId,
            actorId,
            Cut(actorRole, ActorRoleMaxLength),
            Cut(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), ReasonMaxLength),
            cleanDetails,
            Cut(ipAddress, IpAddressMaxLength),
            Cut(traceId, TraceIdMaxLength));
    }

    private static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
