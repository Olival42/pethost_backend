namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>Situação da denúncia na fila do admin.</summary>
public enum ReviewReportStatus
{
    /// <summary>Esperando o admin analisar.</summary>
    Pending,

    /// <summary>O admin deu razão a quem denunciou: a avaliação foi ocultada.</summary>
    Upheld,

    /// <summary>O admin não viu problema: a avaliação continua.</summary>
    Dismissed,
}

/// <summary>Valores de <see cref="ReviewReportStatus"/> no banco e no JSON.</summary>
public static class ReviewReportStatusValues
{
    public const string Pending = "pending";
    public const string Upheld = "upheld";
    public const string Dismissed = "dismissed";

    public static IReadOnlyList<string> All { get; } = [Pending, Upheld, Dismissed];

    /// <summary>Os dois desfechos que o admin escolhe ao analisar.</summary>
    public static IReadOnlyList<string> Outcomes { get; } = [Upheld, Dismissed];

    public static string ToWire(ReviewReportStatus status) => status switch
    {
        ReviewReportStatus.Pending => Pending,
        ReviewReportStatus.Upheld => Upheld,
        ReviewReportStatus.Dismissed => Dismissed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown report status."),
    };

    public static ReviewReportStatus FromWire(string value) =>
        TryFromWire(value, out var status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report status.");

    /// <summary>A situação do texto, exatamente como na lista (minúsculo). Para validar a entrada.</summary>
    public static bool TryFromWire(string? value, out ReviewReportStatus status)
    {
        (var known, status) = value switch
        {
            Pending => (true, ReviewReportStatus.Pending),
            Upheld => (true, ReviewReportStatus.Upheld),
            Dismissed => (true, ReviewReportStatus.Dismissed),
            _ => (false, default),
        };

        return known;
    }

    /// <summary>O texto é um desfecho que o admin pode escolher (<c>upheld</c> ou <c>dismissed</c>).</summary>
    public static bool TryOutcomeFromWire(string? value, out ReviewReportStatus outcome) =>
        TryFromWire(value, out outcome) && outcome != ReviewReportStatus.Pending;
}
