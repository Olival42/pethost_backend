namespace PetHost.Modules.Conversations.Domain.Reports;

/// <summary>Situação da denúncia na fila do admin.</summary>
public enum ReportStatus
{
    /// <summary>Esperando o admin analisar. Segura a conversa na retenção.</summary>
    Pending,

    /// <summary>O admin analisou e deu razão a quem denunciou.</summary>
    Upheld,

    /// <summary>O admin analisou e não viu problema.</summary>
    Dismissed,
}

/// <summary>Valores de <see cref="ReportStatus"/> no banco e no JSON.</summary>
public static class ReportStatusValues
{
    public const string Pending = "pending";
    public const string Upheld = "upheld";
    public const string Dismissed = "dismissed";

    public static IReadOnlyList<string> All { get; } = [Pending, Upheld, Dismissed];

    /// <summary>Os dois desfechos que o admin escolhe ao analisar.</summary>
    public static IReadOnlyList<string> Outcomes { get; } = [Upheld, Dismissed];

    public static string ToWire(ReportStatus status) => status switch
    {
        ReportStatus.Pending => Pending,
        ReportStatus.Upheld => Upheld,
        ReportStatus.Dismissed => Dismissed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown report status."),
    };

    public static ReportStatus FromWire(string value) =>
        TryFromWire(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report status.");

    /// <summary>A situação do texto, exatamente como na lista (minúsculo). Para validar a entrada.</summary>
    public static bool TryFromWire(string? value, out ReportStatus status)
    {
        (var known, status) = value switch
        {
            Pending => (true, ReportStatus.Pending),
            Upheld => (true, ReportStatus.Upheld),
            Dismissed => (true, ReportStatus.Dismissed),
            _ => (false, default),
        };

        return known;
    }

    /// <summary>O texto é um desfecho que o admin pode escolher (<c>upheld</c> ou <c>dismissed</c>).</summary>
    public static bool TryOutcomeFromWire(string? value, out ReportStatus outcome) =>
        TryFromWire(value, out outcome) && outcome != ReportStatus.Pending;
}
