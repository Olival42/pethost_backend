namespace PetHost.Modules.Conversations.Presentation.Reports;

/// <summary>Query string de <c>GET /api/v1/conversations/reports</c>.</summary>
public sealed class MessageReportsRequest
{
    /// <summary>
    /// <c>pending</c> (padrão: a fila), <c>upheld</c>, <c>dismissed</c> ou <c>all</c> (todas).
    /// </summary>
    public string Status { get; init; } = "pending";

    /// <summary>Começa em 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = 20;
}
