using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Abstractions;

/// <summary>
/// Porta para o Stripe (cobranças, reembolsos, repasses). A Infrastructure implementa sobre o
/// <c>Stripe.net</c>; nos testes, um dublê. Toda escrita leva chave de idempotência: repetir o
/// mesmo pedido (retry, webhook duplicado) devolve o mesmo objeto, nunca cobra ou repassa duas vezes.
/// </summary>
/// <remarks>
/// Stripe fora do ar, lento ou sem chave configurada: <c>503 PAYMENT_STRIPE_UNAVAILABLE</c>.
/// Pedido recusado pelo Stripe (dado que ele não aceita, estado que não permite):
/// <c>422 PAYMENT_STRIPE_REQUEST_REJECTED</c>.
/// </remarks>
public interface IStripePayments
{
    /// <summary>A chave publicável, que o app usa com o <c>clientSecret</c> no Payment Element.</summary>
    string PublishableKey { get; }

    /// <summary>
    /// Cria a cobrança de uma parcela: cartão e Pix, em BRL, ligada à reserva (<c>transfer_group</c>),
    /// com o QR code do Pix vencendo em <see cref="IntentDraft.PixExpiresAt"/>.
    /// </summary>
    Task<Result<StripeCheckout>> CreateIntentAsync(IntentDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Reaproveita a cobrança já criada: relê e, se ela está esperando o tutor escolher o meio de
    /// pagamento (o Pix anterior venceu, o cartão foi recusado), atualiza o valor para
    /// <paramref name="amountCents"/> (o acréscimo por atraso cresce a cada dia) e renova o prazo do
    /// Pix. Com um QR code do Pix ainda valendo, nada muda: o valor dele continua o mesmo.
    /// </summary>
    Task<Result<StripeCheckout>> PrepareIntentAsync(string paymentIntentId, long amountCents, DateTimeOffset pixExpiresAt, CancellationToken cancellationToken);

    /// <summary>Cancela a cobrança em aberto. Já paga ou em processamento: recusado.</summary>
    Task<Result> CancelIntentAsync(string paymentIntentId, CancellationToken cancellationToken);

    /// <summary>Relê a cobrança da parcela: situação, meio, cobrança efetiva, reembolso e contestação.</summary>
    Task<Result<ChargeSnapshot>> GetChargeAsync(string paymentIntentId, CancellationToken cancellationToken);

    /// <summary>
    /// A cobrança (<c>pi_...</c>) de um objeto que chegou num webhook: o próprio <c>pi_</c>, ou a
    /// cobrança de uma <c>ch_</c>, de um reembolso (<c>re_</c>) ou de uma contestação (<c>dp_</c>).
    /// <c>null</c> quando o objeto não pertence a uma cobrança.
    /// </summary>
    Task<Result<string?>> ResolveIntentIdAsync(string stripeObjectId, CancellationToken cancellationToken);

    /// <summary>Devolve ao tutor parte ou todo o valor de uma parcela.</summary>
    Task<Result<RefundSnapshot>> RefundAsync(RefundDraft draft, CancellationToken cancellationToken);

    /// <summary>Transfere a parte do anfitrião para a conta conectada dele, amarrada à cobrança de origem.</summary>
    /// <returns>O id da transferência (<c>tr_...</c>).</returns>
    Task<Result<string>> TransferAsync(TransferDraft draft, CancellationToken cancellationToken);

    /// <summary>Estorna (total ou parcialmente) uma transferência ao anfitrião.</summary>
    /// <returns>O id do estorno (<c>trr_...</c>).</returns>
    Task<Result<string>> ReverseTransferAsync(ReversalDraft draft, CancellationToken cancellationToken);
}

/// <summary>A cobrança de uma parcela a criar.</summary>
/// <param name="InstallmentId">A parcela: base da chave de idempotência.</param>
/// <param name="Attempt">Quantas cobranças a parcela já teve, contando esta (cobrança cancelada não volta).</param>
/// <param name="AmountCents">Quanto cobrar: o valor da parcela mais o acréscimo por atraso.</param>
/// <param name="LateFeeCents">Quanto de <paramref name="AmountCents"/> é multa e juros (vai nos metadados).</param>
public sealed record IntentDraft(
    Guid InstallmentId,
    Guid PaymentId,
    Guid BookingId,
    int InstallmentNumber,
    int InstallmentCount,
    int Attempt,
    long AmountCents,
    DateTimeOffset PixExpiresAt,
    long LateFeeCents = 0);

/// <summary>O que o app precisa para abrir o Payment Element.</summary>
/// <param name="ClientSecret">Segredo da cobrança, entregue só ao tutor dono dela.</param>
/// <param name="AmountCents">Quanto a cobrança pede, em centavos.</param>
public sealed record StripeCheckout(string PaymentIntentId, string ClientSecret, ChargeState State, long AmountCents);

/// <summary>Um reembolso a pedir.</summary>
public sealed record RefundDraft(Guid InstallmentId, Guid PaymentId, int InstallmentNumber, string PaymentIntentId, long AmountCents);

/// <summary>Um repasse a fazer.</summary>
/// <param name="ChargeId">A cobrança de origem (<c>source_transaction</c>): o repasse usa o dinheiro dela.</param>
/// <param name="DestinationAccountId">A conta conectada do anfitrião (<c>acct_...</c>).</param>
public sealed record TransferDraft(
    Guid InstallmentId,
    Guid PaymentId,
    Guid BookingId,
    int InstallmentNumber,
    string ChargeId,
    string DestinationAccountId,
    long AmountCents);

/// <summary>Um estorno de repasse a fazer.</summary>
public sealed record ReversalDraft(Guid InstallmentId, Guid PaymentId, int InstallmentNumber, string TransferId, long AmountCents);
