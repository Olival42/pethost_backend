namespace PetHost.Modules.Conversations.Domain.Reports;

/// <summary>Por que a mensagem foi denunciada.</summary>
public enum ReportReason
{
    /// <summary>Propaganda ou mensagem repetida.</summary>
    Spam,

    /// <summary>Assédio, ofensa ou ameaça.</summary>
    Harassment,

    /// <summary>Golpe: pedido de pagamento por fora, link suspeito.</summary>
    Scam,

    /// <summary>Conteúdo impróprio (foto ou texto).</summary>
    Inappropriate,

    /// <summary>Outro motivo: a descrição é obrigatória.</summary>
    Other,
}

/// <summary>Valores de <see cref="ReportReason"/> no banco e no JSON.</summary>
public static class ReportReasonValues
{
    public const string Spam = "spam";
    public const string Harassment = "harassment";
    public const string Scam = "scam";
    public const string Inappropriate = "inappropriate";
    public const string Other = "other";

    public static IReadOnlyList<string> All { get; } = [Spam, Harassment, Scam, Inappropriate, Other];

    public static string ToWire(ReportReason reason) => reason switch
    {
        ReportReason.Spam => Spam,
        ReportReason.Harassment => Harassment,
        ReportReason.Scam => Scam,
        ReportReason.Inappropriate => Inappropriate,
        ReportReason.Other => Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown report reason."),
    };

    public static ReportReason FromWire(string value) =>
        TryFromWire(value, out var reason)
            ? reason
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report reason.");

    /// <summary>O motivo do texto, exatamente como na lista (minúsculo). Para validar a entrada.</summary>
    public static bool TryFromWire(string? value, out ReportReason reason)
    {
        (var known, reason) = value switch
        {
            Spam => (true, ReportReason.Spam),
            Harassment => (true, ReportReason.Harassment),
            Scam => (true, ReportReason.Scam),
            Inappropriate => (true, ReportReason.Inappropriate),
            Other => (true, ReportReason.Other),
            _ => (false, default),
        };

        return known;
    }
}
