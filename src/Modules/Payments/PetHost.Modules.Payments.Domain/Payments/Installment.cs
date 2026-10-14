namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// Uma parcela do pagamento da reserva — uma cobrança no Stripe (um <c>PaymentIntent</c>), com o
/// reembolso, o repasse ao anfitrião e a contestação dela. Faz parte do agregado
/// <see cref="Payment"/> e só muda por ele. Espelha <c>payment.payment_installments</c>.
/// </summary>
/// <remarks>
/// Cada parcela tem uma cobrança, um reembolso, um repasse e uma contestação no máximo: o Stripe
/// liga reembolso, contestação e transferência à cobrança de origem, então tudo fica na mesma linha.
/// </remarks>
public sealed class Installment
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Installment()
    {
    }

    internal Installment(int number, long amountCents, DateTimeOffset dueAt)
    {
        Id = Guid.CreateVersion7();
        Number = number;
        AmountCents = amountCents;
        DueAt = dueAt.ToUniversalTime();
        Status = InstallmentStatus.Pending;
        RefundStatus = RefundStatus.None;
        PayoutStatus = PayoutStatus.Held;
        DisputeStatus = DisputeStatus.None;
    }

    /// <summary>
    /// Identidade da parcela (UUID v7). Trocar o plano cria parcelas novas, com ids novos; é também a
    /// base das chaves de idempotência no Stripe.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>1 a 6, na ordem de vencimento.</summary>
    public int Number { get; private set; }

    /// <summary>Valor da parcela, em centavos (sem acréscimo por atraso).</summary>
    public long AmountCents { get; private set; }

    /// <summary>
    /// Multa e juros por atraso que entraram na cobrança, em centavos: o que a cobrança aberta pede
    /// a mais e, depois de paga, o que o tutor pagou a mais. Zero para parcela paga em dia.
    /// </summary>
    public long LateFeeCents { get; private set; }

    /// <summary>O que o tutor paga (ou pagou) pela parcela: o valor dela mais o acréscimo por atraso.</summary>
    public long ChargedCents => AmountCents + LateFeeCents;

    /// <summary>
    /// Vencimento. A 1ª vence no prazo de pagamento da reserva (24h depois do aceite); as outras, no
    /// fim do dia (Brasília), a cada 30 dias.
    /// </summary>
    public DateTimeOffset DueAt { get; private set; }

    public InstallmentStatus Status { get; private set; }

    /// <summary>Cartão ou Pix, quando o tutor pagou (ou começou a pagar).</summary>
    public PaymentMethod? Method { get; private set; }

    /// <summary>A cobrança no Stripe (<c>pi_...</c>), criada quando o tutor abre o pagamento.</summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>
    /// A cobrança do Stripe ainda pode ser paga (não foi paga nem cancelada). Parcela que não será
    /// mais cobrada e ainda tem cobrança aberta precisa dela cancelada no Stripe.
    /// </summary>
    public bool IntentOpen { get; private set; }

    /// <summary>Quantas cobranças já foram criadas para a parcela (entra na chave de idempotência).</summary>
    public int PaymentAttempts { get; private set; }

    /// <summary>A cobrança efetiva (<c>ch_...</c>): origem do repasse ao anfitrião.</summary>
    public string? StripeChargeId { get; private set; }

    /// <summary>Por que a última tentativa falhou (código do Stripe, ex.: <c>card_declined</c>).</summary>
    public string? FailureCode { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>Quando o lembrete de vencimento foi enviado ao tutor.</summary>
    public DateTimeOffset? ReminderSentAt { get; private set; }

    /// <summary>Desde quando está em atraso (venceu depois do check-in e passou da tolerância).</summary>
    public DateTimeOffset? OverdueSince { get; private set; }

    /// <summary>Quando o tutor recebeu o último lembrete de parcela em atraso (um a cada 7 dias).</summary>
    public DateTimeOffset? OverdueRemindedAt { get; private set; }

    public RefundStatus RefundStatus { get; private set; }

    /// <summary>Quanto foi (ou está sendo) devolvido ao tutor, em centavos.</summary>
    public long RefundedCents { get; private set; }

    public string? StripeRefundId { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    public PayoutStatus PayoutStatus { get; private set; }

    /// <summary>A parte do anfitrião: 90% do que ficou retido (acréscimo por atraso incluído). Gravada no repasse.</summary>
    public long HostPayoutCents { get; private set; }

    /// <summary>A parte do PetHost: 10% do que ficou retido (é dela que sai a taxa do Stripe).</summary>
    public long PlatformFeeCents { get; private set; }

    /// <summary>A transferência para a conta conectada do anfitrião (<c>tr_...</c>).</summary>
    public string? StripeTransferId { get; private set; }

    public DateTimeOffset? TransferredAt { get; private set; }

    /// <summary>Por que o último repasse falhou (a manutenção tenta de novo).</summary>
    public string? PayoutFailureCode { get; private set; }

    /// <summary>Quanto do repasse foi estornado do anfitrião (contestação perdida), em centavos.</summary>
    public long ReversedCents { get; private set; }

    public string? StripeTransferReversalId { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public DisputeStatus DisputeStatus { get; private set; }

    public string? StripeDisputeId { get; private set; }

    public DateTimeOffset? DisputedAt { get; private set; }

    public DateTimeOffset? DisputeClosedAt { get; private set; }

    /// <summary>O Stripe confirmou o pagamento.</summary>
    public bool IsPaid => Status == InstallmentStatus.Paid;

    /// <summary>Ainda pode ser paga: a vencer, vencida na tolerância ou em atraso.</summary>
    public bool IsOpen => Status is InstallmentStatus.Pending or InstallmentStatus.Overdue;

    /// <summary>
    /// Chegou ao fim: paga com reembolso, repasse e contestação resolvidos, ou cancelada sem
    /// cobrança aberta. Uma contestação ainda pode chegar depois — o webhook acha a parcela do mesmo jeito.
    /// </summary>
    public bool IsSettled => Status switch
    {
        InstallmentStatus.Cancelled => !IntentOpen,
        InstallmentStatus.Paid => DisputeStatus != DisputeStatus.Open
            && RefundStatus is not (RefundStatus.Pending or RefundStatus.Failed)
            && PayoutStatus != PayoutStatus.Held,
        _ => false,
    };

    internal void AttachIntent(string paymentIntentId)
    {
        StripePaymentIntentId = paymentIntentId;
        IntentOpen = true;
        PaymentAttempts++;
    }

    internal void Cancel()
    {
        Status = InstallmentStatus.Cancelled;
        if (!IsPaid)
            PayoutStatus = PayoutStatus.Cancelled;
    }

    internal void MarkIntentCancelled() => IntentOpen = false;

    internal void MarkOverdue(DateTimeOffset now)
    {
        Status = InstallmentStatus.Overdue;
        OverdueSince = now;
    }

    internal void MarkReminded(DateTimeOffset now) => ReminderSentAt = now;

    internal void MarkOverdueReminded(DateTimeOffset now) => OverdueRemindedAt = now;

    /// <summary>A cobrança pede <paramref name="chargeCents"/>: o que passa do valor da parcela é acréscimo.</summary>
    internal void SetCharge(long chargeCents) => LateFeeCents = Math.Max(0, chargeCents - AmountCents);

    internal void CancelPayout() => PayoutStatus = PayoutStatus.Cancelled;

    internal void RecordRefund(string refundId, long amountCents, RefundStatus status, DateTimeOffset now)
    {
        StripeRefundId = refundId;
        RefundedCents = amountCents;
        RefundStatus = status;
        RefundedAt ??= now;
    }

    internal void RecordTransfer(string transferId, long hostCents, long platformCents, DateTimeOffset now)
    {
        StripeTransferId = transferId;
        HostPayoutCents = hostCents;
        PlatformFeeCents = platformCents;
        PayoutStatus = PayoutStatus.Transferred;
        PayoutFailureCode = null;
        TransferredAt = now;
    }

    internal void RecordTransferFailed(string code) => PayoutFailureCode = code;

    internal void RecordReversal(string reversalId, long amountCents, DateTimeOffset now)
    {
        StripeTransferReversalId = reversalId;
        ReversedCents += amountCents;
        PayoutStatus = PayoutStatus.Reversed;
        PayoutFailureCode = null;
        ReversedAt = now;
    }

    /// <summary>
    /// Aplica a cobrança como o Stripe a vê agora. Pago não "despaga"; cancelada continua cancelada
    /// (se o Stripe confirmar uma cobrança de parcela cancelada, ela vira paga e o dinheiro volta
    /// pelo reembolso).
    /// </summary>
    internal InstallmentChange Apply(ChargeSnapshot snapshot, DateTimeOffset now)
    {
        var wasPaid = IsPaid;
        var previousDispute = DisputeStatus;
        var previousRefund = RefundStatus;

        Method = snapshot.Method ?? Method;
        StripeChargeId = snapshot.ChargeId ?? StripeChargeId;

        // O valor pago não muda depois de pago; antes disso, vale o que a cobrança pede agora.
        if (snapshot.AmountCents is { } charged && !wasPaid)
            SetCharge(charged);

        switch (snapshot.State)
        {
            case ChargeState.Succeeded:
                if (!IsPaid)
                {
                    Status = InstallmentStatus.Paid;
                    PaidAt = now;
                    PayoutStatus = PayoutStatus.Held;
                }

                FailureCode = null;
                IntentOpen = false;
                break;

            case ChargeState.Processing:
                if (IsOpen)
                    Status = InstallmentStatus.Processing;

                IntentOpen = true;
                break;

            case ChargeState.AwaitingPayment:
                ReturnToOpen();
                FailureCode = snapshot.FailureCode ?? FailureCode;
                IntentOpen = true;
                break;

            case ChargeState.Canceled:
                ReturnToOpen();
                IntentOpen = false;
                break;
        }

        if (snapshot.Refund is { } refund)
            RecordRefund(refund.Id, refund.AmountCents, refund.Status, now);

        if (snapshot.Dispute is { } dispute)
        {
            StripeDisputeId = dispute.Id;
            DisputeStatus = dispute.Status;
            DisputedAt ??= now;

            if (dispute.Status is DisputeStatus.Won or DisputeStatus.Lost)
                DisputeClosedAt ??= now;
        }

        return new InstallmentChange(
            Number,
            BecamePaid: !wasPaid && IsPaid,
            DisputeOpened: previousDispute == DisputeStatus.None && DisputeStatus != DisputeStatus.None,
            DisputeClosed: previousDispute is DisputeStatus.None or DisputeStatus.Open && DisputeStatus is DisputeStatus.Won or DisputeStatus.Lost,
            Dispute: DisputeStatus,
            RefundFailed: previousRefund != RefundStatus.Failed && RefundStatus == RefundStatus.Failed);
    }

    /// <summary>A tentativa não foi adiante (falhou, venceu o Pix): a parcela volta a poder ser paga.</summary>
    private void ReturnToOpen()
    {
        if (Status == InstallmentStatus.Processing)
            Status = OverdueSince is null ? InstallmentStatus.Pending : InstallmentStatus.Overdue;
    }
}
