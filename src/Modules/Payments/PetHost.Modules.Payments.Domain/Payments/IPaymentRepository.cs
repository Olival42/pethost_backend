namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Acesso a <see cref="Payment"/>. A Application só conhece esta interface (§11).</summary>
public interface IPaymentRepository
{
    /// <summary>O pagamento da reserva, com as parcelas. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Payment?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>O pagamento que tem a cobrança <paramref name="paymentIntentId"/> (<c>pi_...</c>). Rastreado.</summary>
    Task<Payment?> GetByIntentAsync(string paymentIntentId, CancellationToken cancellationToken);

    /// <summary>
    /// As reservas dos pagamentos que ainda têm o que fazer (<see cref="Payment.SettledAt"/> nulo),
    /// os menos atualizados primeiro, no máximo <paramref name="limit"/>. Só os ids: a manutenção
    /// carrega e grava um pagamento por vez, para um conflito não derrubar os outros.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListUnsettledBookingIdsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Pagamentos do anfitrião com alguma parcela paga, paginados, do mais recente para o mais antigo.</summary>
    Task<(IReadOnlyList<Payment> Items, long TotalCount)> SearchPaidByHostAsync(
        Guid hostId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Todos os pagamentos do anfitrião com alguma parcela paga, sem ordem — a base dos ganhos do
    /// painel. Não rastreados.
    /// </summary>
    Task<IReadOnlyList<Payment>> ListPaidByHostAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Pagamentos que pedem atenção do admin: parcela em atraso, contestação aberta ou perdida,
    /// reembolso que falhou ou repasse que não passou.
    /// </summary>
    Task<IReadOnlyList<Payment>> ListNeedingAttentionAsync(CancellationToken cancellationToken);

    /// <summary>O tutor tem alguma parcela em atraso?</summary>
    Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>O tutor já pagou alguma parcela (<c>paid_at</c> preenchido, mesmo se reembolsada depois)?</summary>
    Task<bool> HasPaidAnyInstallmentAsync(Guid ownerId, CancellationToken cancellationToken);

    void Add(Payment payment);
}
