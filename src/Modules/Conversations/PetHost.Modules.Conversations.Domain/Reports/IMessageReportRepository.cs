namespace PetHost.Modules.Conversations.Domain.Reports;

/// <summary>Acesso a <see cref="MessageReport"/>. A Application só conhece esta interface (§11).</summary>
public interface IMessageReportRepository
{
    /// <summary>Pelo id. Rastreado: quem chama pode analisar e gravar.</summary>
    Task<MessageReport?> GetByIdAsync(MessageReportId id, CancellationToken cancellationToken);

    void Add(MessageReport report);
}
