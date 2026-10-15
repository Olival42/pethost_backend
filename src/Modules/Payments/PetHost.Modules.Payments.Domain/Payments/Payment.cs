using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// O pagamento de uma reserva: o plano de parcelas que o tutor escolheu (1 a 6), cada parcela com
/// a sua cobrança no Stripe, e o destino do dinheiro — reembolso ao tutor, repasse ao anfitrião
/// ou o que a contestação decidir. Espelha <c>payment.payments</c> e <c>payment.payment_installments</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cobrança e transferência separadas</b> (escopo, seção 6): o tutor paga o PetHost, que guarda
/// o dinheiro. No check-out, cada parcela paga gera um repasse de <see cref="HostSharePercentage"/>%
/// ao anfitrião, amarrado à cobrança de origem; os outros 10% ficam com o PetHost (e pagam a taxa
/// do Stripe). Parcela paga depois do check-out é repassada assim que é paga.
/// </para>
/// <para>
/// <b>Parcelas</b>: a 1ª vence no prazo de pagamento da reserva (24h depois do aceite) e confirma a
/// reserva; as outras vencem a cada <see cref="InstallmentIntervalDays"/> dias, inclusive depois da
/// estadia. Vencida antes do check-in e passada a tolerância (<see cref="OverdueGrace"/>), a reserva
/// é cancelada com 100% de reembolso; depois do check-in, a parcela fica em atraso.
/// </para>
/// <para>
/// <b>Atraso</b>: parcela paga depois da tolerância tem multa de <see cref="LateFinePercentage"/>% e
/// juros de <see cref="MonthlyInterestPercentage"/>% ao mês por dia desde o vencimento
/// (<see cref="LateFeeFor"/>). O acréscimo entra na cobrança e segue a parcela: reembolso e repasse
/// (90/10) contam sobre o que o tutor pagou.
/// </para>
/// <para>
/// O agregado decide (<see cref="Reconcile"/> devolve os pedidos ao Stripe) e registra os resultados
/// (<c>Record...</c>); quem chama o Stripe é a Application. Assim tudo é testável sem rede, e repetir
/// a reconciliação nunca repete um pedido já registrado.
/// </para>
/// Guarda ids de outros módulos como FK lógica: reserva (<c>booking.bookings.id</c>), tutor
/// (<c>owner.owners.id</c>) e anfitrião (<c>host.hosts.id</c>).
/// </remarks>
public sealed class Payment : Entity<PaymentId>
{
    /// <summary>Máximo de parcelas.</summary>
    public const int MaxInstallments = 6;

    /// <summary>Valor mínimo de cada parcela: R$ 50,00. Reserva menor que isso só à vista.</summary>
    public const long MinInstallmentCents = 5_000;

    /// <summary>A parte do anfitrião em cada parcela retida (o resto fica com o PetHost).</summary>
    public const int HostSharePercentage = 90;

    /// <summary>Reembolso inteiro.</summary>
    public const int FullRefundPercentage = 100;

    /// <summary>Dias entre uma parcela e a seguinte.</summary>
    public const int InstallmentIntervalDays = 30;

    /// <summary>Moeda de toda cobrança no MVP.</summary>
    public const string Currency = "brl";

    /// <summary>Tolerância depois do vencimento (parcelas da 2ª em diante) antes de cancelar ou marcar atraso.</summary>
    public static readonly TimeSpan OverdueGrace = TimeSpan.FromHours(24);

    /// <summary>Com quanta antecedência do vencimento o tutor recebe o lembrete.</summary>
    public static readonly TimeSpan ReminderNotice = TimeSpan.FromDays(3);

    /// <summary>Multa por atraso, em % da parcela (o CDC limita a 2%).</summary>
    public const int LateFinePercentage = 2;

    /// <summary>Juros de mora, em % ao mês, simples, contados por dia (mês de 30 dias).</summary>
    public const int MonthlyInterestPercentage = 1;

    /// <summary>De quanto em quanto tempo o tutor é lembrado de uma parcela em atraso.</summary>
    public static readonly TimeSpan OverdueReminderInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// Depois de quanto tempo em atraso o admin é avisado (uma vez) e os lembretes ao tutor param:
    /// 4 semanas, ou seja, o aviso de atraso e mais 3 lembretes semanais.
    /// </summary>
    public static readonly TimeSpan OverdueEscalationAfter = TimeSpan.FromDays(28);

    /// <summary>Validade do QR code do Pix gerado ao abrir o pagamento.</summary>
    public static readonly TimeSpan PixLifetime = TimeSpan.FromHours(24);

    /// <summary>O Stripe exige ao menos este tempo até o Pix vencer.</summary>
    private static readonly TimeSpan MinPixLifetime = TimeSpan.FromMinutes(1);

    private readonly List<Installment> _installments = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Payment()
    {
    }

    private Payment(PayableBooking booking, DateTimeOffset now)
        : base(PaymentId.New())
    {
        BookingId = booking.BookingId;
        OwnerId = booking.OwnerId;
        HostId = booking.HostId;
        AmountCents = booking.AmountCents;
        Status = PaymentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>A reserva paga (<c>booking.bookings.id</c>). Um pagamento por reserva.</summary>
    public Guid BookingId { get; private set; }

    /// <summary>O tutor que paga (<c>owner.owners.id</c>).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>O anfitrião que recebe o repasse (<c>host.hosts.id</c>).</summary>
    public Guid HostId { get; private set; }

    /// <summary>Valor total da reserva, em centavos: a soma das parcelas.</summary>
    public long AmountCents { get; private set; }

    /// <summary>Em quantas parcelas o tutor paga.</summary>
    public int InstallmentCount { get; private set; }

    /// <summary>Quantas vezes o plano foi escolhido (trocar antes de pagar cria parcelas novas).</summary>
    public int PlanRevision { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// Quanto do que foi pago volta ao tutor (0 a 100), copiado da reserva quando ela é cancelada.
    /// Expirada ou recusada (nada deveria ter sido pago): 100.
    /// </summary>
    public int? RefundPercentage { get; private set; }

    public IReadOnlyList<Installment> Installments => _installments.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Quando a reserva foi cancelada ou expirou.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// Quando tudo chegou ao fim (nada mais a cobrar, devolver ou repassar). Nulo enquanto há o que
    /// fazer: é o que a manutenção periódica revisita.
    /// </summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Alguma parcela em atraso (o tutor não pede estadia nova até pagar).</summary>
    public bool HasOverdueInstallment => _installments.Any(i => i.Status == InstallmentStatus.Overdue);

    /// <summary>Ainda dá para trocar o número de parcelas: nada pago nem sendo pago.</summary>
    public bool CanChangePlan =>
        Status == PaymentStatus.Pending
        && _installments.All(i => i.Status is InstallmentStatus.Pending or InstallmentStatus.Cancelled);

    /// <summary>Quanto já foi pago, em centavos (acréscimos por atraso incluídos).</summary>
    public long PaidCents => _installments.Where(i => i.IsPaid).Sum(i => i.ChargedCents);

    /// <summary>Até quantas parcelas o valor permite (cada uma de pelo menos <see cref="MinInstallmentCents"/>).</summary>
    public static int MaxInstallmentsFor(long amountCents) =>
        (int)Math.Clamp(amountCents / MinInstallmentCents, 1, MaxInstallments);

    /// <summary>As formas de parcelar o valor: de 1x até <see cref="MaxInstallmentsFor"/>.</summary>
    public static IReadOnlyList<InstallmentOption> OptionsFor(long amountCents)
    {
        if (amountCents <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountCents), amountCents, "The amount must be positive.");

        return [.. Enumerable.Range(1, MaxInstallmentsFor(amountCents)).Select(count =>
        {
            var amounts = Split(amountCents, count);
            return new InstallmentOption(count, amounts[0], amounts[^1]);
        })];
    }

    /// <summary>
    /// As parcelas (valor e vencimento) de um plano em <paramref name="installmentCount"/> vezes, como
    /// ficariam se o tutor o escolhesse agora: a 1ª vence em <paramref name="paymentDeadline"/>; as
    /// outras, no fim do dia (Brasília), a cada <see cref="InstallmentIntervalDays"/> dias a partir de hoje.
    /// </summary>
    public static IReadOnlyList<PlannedInstallment> ScheduleFor(
        long amountCents, int installmentCount, DateTimeOffset paymentDeadline, DateTimeOffset now)
    {
        if (amountCents <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountCents), amountCents, "The amount must be positive.");

        if (installmentCount < 1 || installmentCount > MaxInstallmentsFor(amountCents))
            throw new ArgumentOutOfRangeException(nameof(installmentCount), installmentCount, "Installment count not allowed for this amount.");

        var today = PaymentClock.Today(now);

        return [.. Split(amountCents, installmentCount).Select((amount, index) => new PlannedInstallment(
            index + 1,
            amount,
            index == 0 ? paymentDeadline.ToUniversalTime() : PaymentClock.EndOf(today.AddDays(InstallmentIntervalDays * index)).ToUniversalTime()))];
    }

    /// <summary>
    /// O tutor escolhe em quantas parcelas paga a reserva aceita. A 1ª vence no prazo de pagamento
    /// da reserva; as outras, a cada 30 dias a partir de hoje. Mais parcelas do que o valor permite:
    /// <c>PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED</c>.
    /// </summary>
    public static Result<Payment> Create(PayableBooking booking, int installmentCount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.AmountCents <= 0)
            throw new ArgumentException("The booking amount must be positive.", nameof(booking));

        var payment = new Payment(booking, now);

        var planned = payment.Plan(installmentCount, booking.PaymentDeadline, now);
        return planned.IsSuccess ? Result<Payment>.Success(payment) : Result<Payment>.FromFailure(planned);
    }

    /// <summary>
    /// Troca o número de parcelas antes de pagar qualquer coisa. As cobranças abertas das parcelas
    /// antigas precisam ser canceladas no Stripe antes (<see cref="OpenIntentIds"/>). Algo pago ou
    /// sendo pago: <c>PAYMENT_PLAN_LOCKED</c>.
    /// </summary>
    public Result ChangePlan(int installmentCount, DateTimeOffset paymentDeadline, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Cancelled)
            return Result.Failure(PaymentsErrors.Closed);

        if (!CanChangePlan)
            return Result.Failure(PaymentsErrors.PlanLocked);

        return Plan(installmentCount, paymentDeadline, now);
    }

    /// <summary>As cobranças ainda abertas no Stripe (para cancelar antes de trocar o plano).</summary>
    public IReadOnlyList<string> OpenIntentIds =>
        [.. _installments.Where(i => i.IntentOpen && i.StripePaymentIntentId is not null).Select(i => i.StripePaymentIntentId!)];

    /// <summary>A parcela de número <paramref name="number"/>, ou <c>null</c>.</summary>
    public Installment? FindInstallment(int number) => _installments.FirstOrDefault(i => i.Number == number);

    /// <summary>A parcela da cobrança <paramref name="paymentIntentId"/>, ou <c>null</c>.</summary>
    public Installment? FindByIntent(string paymentIntentId) =>
        _installments.FirstOrDefault(i => i.StripePaymentIntentId == paymentIntentId);

    /// <summary>
    /// A parcela <paramref name="number"/> pode ser paga agora? Precisa estar em aberto, ser a
    /// primeira não paga (as parcelas são pagas em ordem — adiantar a próxima pode) e, se for a 1ª,
    /// estar no prazo de pagamento da reserva.
    /// </summary>
    public Result<Installment> EnsurePayable(int number, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Cancelled)
            return Result<Installment>.Failure(PaymentsErrors.Closed);

        var installment = FindInstallment(number);
        if (installment is null)
            return Result<Installment>.Failure(PaymentsErrors.InstallmentNotFound(number));

        switch (installment.Status)
        {
            case InstallmentStatus.Paid:
                return Result<Installment>.Failure(PaymentsErrors.InstallmentAlreadyPaid(number));
            case InstallmentStatus.Processing:
                return Result<Installment>.Failure(PaymentsErrors.InstallmentProcessing(number));
            case InstallmentStatus.Cancelled:
                return Result<Installment>.Failure(PaymentsErrors.Closed);
        }

        if (_installments.FirstOrDefault(i => i.Number < number && !i.IsPaid) is { } earlier)
            return Result<Installment>.Failure(PaymentsErrors.InstallmentOutOfOrder(earlier.Number));

        if (number == 1 && now > installment.DueAt)
            return Result<Installment>.Failure(PaymentsErrors.PaymentWindowClosed);

        return Result<Installment>.Success(installment);
    }

    /// <summary>
    /// Até quando o QR code do Pix da parcela vale: <see cref="PixLifetime"/>, sem passar do prazo
    /// da parcela — o vencimento da 1ª (depois dele a reserva expira) e, antes do check-in, o fim da
    /// tolerância das outras (depois dele a reserva é cancelada). Assim o Pix não é pago depois do prazo.
    /// </summary>
    public DateTimeOffset PixExpiresAt(int number, bool beforeCheckIn, DateTimeOffset now)
    {
        var installment = FindInstallment(number)
            ?? throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown installment.");

        DateTimeOffset? cutoff = number == 1
            ? installment.DueAt
            : beforeCheckIn ? installment.DueAt + OverdueGrace : null;

        var expiresAt = now + PixLifetime;
        if (cutoff is { } limit && limit < expiresAt)
            expiresAt = limit;

        return expiresAt < now + MinPixLifetime ? now + MinPixLifetime : expiresAt;
    }

    /// <summary>
    /// Multa e juros de uma parcela paga em <paramref name="now"/>, com vencimento em
    /// <paramref name="dueAt"/>. Dentro da tolerância (<see cref="OverdueGrace"/>): nada. Depois:
    /// <see cref="LateFinePercentage"/>% de multa e <see cref="MonthlyInterestPercentage"/>% ao mês de
    /// juros pelos dias de calendário (Brasília) desde o dia do vencimento — pagar no dia seguinte ao
    /// fim da tolerância conta 2 dias. Arredonda o meio para cima.
    /// </summary>
    public static LateFee LateFeeFor(long amountCents, DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (now < dueAt + OverdueGrace)
            return LateFee.None;

        // O vencimento é o fim do dia (a meia-noite seguinte): o dia dele é o anterior a essa meia-noite.
        var dueDate = PaymentClock.Today(dueAt.AddTicks(-1));
        var days = PaymentClock.Today(now).DayNumber - dueDate.DayNumber;

        var fine = (long)Math.Round(amountCents * LateFinePercentage / 100m, MidpointRounding.AwayFromZero);
        var interest = (long)Math.Round(amountCents * MonthlyInterestPercentage / 100m * days / 30m, MidpointRounding.AwayFromZero);

        return new LateFee(days, fine, interest);
    }

    /// <summary>
    /// O acréscimo que a parcela <paramref name="number"/> teria se fosse paga agora. Só da 2ª em
    /// diante e ainda em aberto: a 1ª nunca atrasa (a reserva expira) e paga não muda mais.
    /// </summary>
    public LateFee LateFeeDue(int number, DateTimeOffset now)
    {
        var installment = Require(number);

        return installment.Number > 1 && installment.IsOpen
            ? LateFeeFor(installment.AmountCents, installment.DueAt, now)
            : LateFee.None;
    }

    /// <summary>Quanto o tutor paga pela parcela agora: o valor dela mais o acréscimo por atraso.</summary>
    public long AmountDue(int number, DateTimeOffset now) => Require(number).AmountCents + LateFeeDue(number, now).TotalCents;

    /// <summary>
    /// A cobrança da parcela pede <paramref name="chargeCents"/> (criada ou atualizada com o
    /// acréscimo do dia). O que passar do valor da parcela fica gravado como acréscimo.
    /// </summary>
    public void RecordCharge(int number, long chargeCents, DateTimeOffset now)
    {
        var installment = Require(number);
        if (installment.IsPaid)
            return;

        installment.SetCharge(chargeCents);
        Touch(now);
    }

    /// <summary>Liga a cobrança criada no Stripe à parcela.</summary>
    public void AttachIntent(int number, string paymentIntentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);

        var installment = FindInstallment(number)
            ?? throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown installment.");

        installment.AttachIntent(paymentIntentId);
        Touch(now);
    }

    /// <summary>
    /// Aplica a cobrança da parcela como o Stripe a vê agora (pagamento, falha, reembolso,
    /// contestação). Idempotente: aplicar o mesmo retrato duas vezes não muda nada na segunda.
    /// </summary>
    public InstallmentChange ApplyCharge(int number, ChargeSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var installment = FindInstallment(number)
            ?? throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown installment.");

        var change = installment.Apply(snapshot, now);
        Touch(now);

        return change;
    }

    /// <summary>
    /// Decide o que falta pedir ao Stripe, dada a reserva agora:
    /// <list type="bullet">
    /// <item>Reserva <b>cancelada</b> ou <b>fechada</b>: as parcelas em aberto são canceladas (e as
    /// cobranças abertas também); cada parcela paga devolve o percentual da reserva sobre o valor dela,
    /// e o que fica retido é repassado 90/10.</item>
    /// <item>Reserva <b>concluída</b>: cada parcela paga é repassada ao anfitrião (90%).</item>
    /// <item>Contestação <b>aberta</b>: nada se move naquela parcela. <b>Perdida</b>: o repasse é
    /// cancelado ou, se já feito, estornado do anfitrião.</item>
    /// </list>
    /// Reembolso vem antes do repasse: o repasse da parcela só sai na reconciliação seguinte, depois
    /// que o reembolso foi registrado. Repetir não repete pedido já registrado.
    /// </summary>
    /// <param name="refundPercentage">O reembolso da reserva cancelada (nulo: nada foi pago quando cancelou — vale 100).</param>
    public IReadOnlyList<PaymentAction> Reconcile(BookingStage stage, int? refundPercentage, DateTimeOffset now)
    {
        if (stage is BookingStage.Cancelled or BookingStage.Closed && Status != PaymentStatus.Cancelled)
        {
            Status = PaymentStatus.Cancelled;
            RefundPercentage = stage == BookingStage.Cancelled ? refundPercentage ?? FullRefundPercentage : FullRefundPercentage;
            CancelledAt = now;
        }

        var actions = new List<PaymentAction>();

        foreach (var installment in _installments)
        {
            if (Status == PaymentStatus.Cancelled && installment.IsOpen)
                installment.Cancel();

            if (installment.Status == InstallmentStatus.Cancelled && installment.IntentOpen && installment.StripePaymentIntentId is { } openIntent)
                actions.Add(new PaymentAction(PaymentActionKind.CancelIntent, installment.Number, openIntent));

            if (installment.IsPaid)
                DecidePaid(installment, stage, actions);
        }

        Touch(now);
        return actions;
    }

    private void DecidePaid(Installment installment, BookingStage stage, List<PaymentAction> actions)
    {
        switch (installment.DisputeStatus)
        {
            case DisputeStatus.Open:
                return;

            case DisputeStatus.Lost:
                if (installment.PayoutStatus == PayoutStatus.Held)
                    installment.CancelPayout();
                else if (installment.PayoutStatus == PayoutStatus.Transferred && installment.StripeTransferId is { } transfer)
                    actions.Add(new PaymentAction(PaymentActionKind.ReverseTransfer, installment.Number, transfer, installment.HostPayoutCents - installment.ReversedCents));

                return;
        }

        if (Status == PaymentStatus.Cancelled)
        {
            if (installment.RefundStatus == RefundStatus.None)
            {
                var refund = RefundFor(installment.ChargedCents, RefundPercentage ?? FullRefundPercentage);
                if (refund > 0)
                {
                    actions.Add(new PaymentAction(PaymentActionKind.Refund, installment.Number, installment.StripePaymentIntentId!, refund));
                    return;
                }
            }

            // Reembolso que falhou: o admin resolve antes de qualquer repasse.
            if (installment.RefundStatus == RefundStatus.Failed)
                return;

            AddPayoutIfDue(installment, actions);
            return;
        }

        if (stage == BookingStage.Completed)
            AddPayoutIfDue(installment, actions);
    }

    private static void AddPayoutIfDue(Installment installment, List<PaymentAction> actions)
    {
        // Sem a cobrança de origem ainda (releitura pendente), o repasse espera a próxima reconciliação.
        if (installment.PayoutStatus != PayoutStatus.Held || installment.StripeChargeId is null)
            return;

        var retained = installment.ChargedCents - installment.RefundedCents;
        if (retained <= 0)
        {
            installment.CancelPayout();
            return;
        }

        var host = HostShare(retained);
        actions.Add(new PaymentAction(PaymentActionKind.Transfer, installment.Number, installment.StripeChargeId, host, retained - host));
    }

    /// <summary>Quanto volta ao tutor de uma parcela: o percentual sobre o valor dela, arredondando o meio para cima.</summary>
    public static long RefundFor(long amountCents, int refundPercentage) =>
        (long)Math.Round(amountCents * refundPercentage / 100m, MidpointRounding.AwayFromZero);

    /// <summary>A parte do anfitrião (90%) do que ficou retido, arredondada para baixo; o centavo que sobra fica com o PetHost.</summary>
    public static long HostShare(long retainedCents) => retainedCents * HostSharePercentage / 100;

    /// <summary>A cobrança aberta foi cancelada no Stripe.</summary>
    public void RecordIntentCancelled(int number, DateTimeOffset now)
    {
        Require(number).MarkIntentCancelled();
        Touch(now);
    }

    /// <summary>O reembolso foi pedido ao Stripe (pode ainda estar a caminho, no Pix).</summary>
    public void RecordRefund(int number, string refundId, long amountCents, RefundStatus status, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundId);

        Require(number).RecordRefund(refundId, amountCents, status, now);
        Touch(now);
    }

    /// <summary>O repasse foi transferido para a conta conectada do anfitrião.</summary>
    public void RecordTransfer(int number, string transferId, long hostCents, long platformCents, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transferId);

        Require(number).RecordTransfer(transferId, hostCents, platformCents, now);
        Touch(now);
    }

    /// <summary>O repasse não passou (sem conta conectada, conta bloqueada): fica retido e a manutenção tenta de novo.</summary>
    public void RecordTransferFailed(int number, string code, DateTimeOffset now)
    {
        Require(number).RecordTransferFailed(code);
        Touch(now);
    }

    /// <summary>O repasse foi estornado do anfitrião (contestação perdida).</summary>
    public void RecordReversal(int number, string reversalId, long amountCents, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reversalId);

        Require(number).RecordReversal(reversalId, amountCents, now);
        Touch(now);
    }

    /// <summary>
    /// Confere as parcelas da 2ª em diante que venceram e passaram da tolerância. Antes do check-in
    /// (reserva aceita ou confirmada): pede o cancelamento da reserva. Depois (em andamento ou
    /// concluída): marca a parcela em atraso — ela continua podendo ser paga.
    /// </summary>
    public OverdueCheck CheckOverdue(BookingStage stage, DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Active or PaymentStatus.Pending))
            return OverdueCheck.None;

        var late = _installments
            .Where(i => i.Number > 1 && i.Status == InstallmentStatus.Pending && now >= i.DueAt + OverdueGrace)
            .ToList();

        if (late.Count == 0)
            return OverdueCheck.None;

        switch (stage)
        {
            case BookingStage.AwaitingPayment or BookingStage.Confirmed:
                return new OverdueCheck(true, []);

            case BookingStage.InProgress or BookingStage.Completed:
                foreach (var installment in late)
                    installment.MarkOverdue(now);

                Touch(now);
                return new OverdueCheck(false, [.. late.Select(i => i.Number)]);

            default:
                return OverdueCheck.None;
        }
    }

    /// <summary>
    /// As parcelas (da 2ª em diante) que vencem nos próximos <see cref="ReminderNotice"/> e ainda não
    /// tiveram lembrete. Já marcadas como lembradas: chamar de novo não repete.
    /// </summary>
    public IReadOnlyList<Installment> TakeReminders(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Active or PaymentStatus.Pending))
            return [];

        var due = _installments
            .Where(i => i.Number > 1
                && i.Status == InstallmentStatus.Pending
                && i.ReminderSentAt is null
                && now >= i.DueAt - ReminderNotice
                && now < i.DueAt)
            .ToList();

        foreach (var installment in due)
            installment.MarkReminded(now);

        if (due.Count > 0)
            Touch(now);

        return due;
    }

    /// <summary>
    /// As parcelas em atraso cujo último aviso (a entrada em atraso ou o lembrete anterior) foi há
    /// <see cref="OverdueReminderInterval"/> ou mais — só enquanto o atraso não passou de
    /// <see cref="OverdueEscalationAfter"/>: depois disso quem cuida é o admin. Marca o lembrete:
    /// chamar de novo não repete.
    /// </summary>
    public IReadOnlyList<Installment> TakeOverdueReminders(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Active or PaymentStatus.Pending))
            return [];

        var due = _installments
            .Where(i => i.Status == InstallmentStatus.Overdue
                && i.OverdueEscalatedAt is null
                && i.OverdueSince is { } since
                && now < since + OverdueEscalationAfter
                && now >= (i.OverdueRemindedAt ?? since) + OverdueReminderInterval)
            .ToList();

        foreach (var installment in due)
            installment.MarkOverdueReminded(now);

        if (due.Count > 0)
            Touch(now);

        return due;
    }

    /// <summary>
    /// As parcelas em atraso há <see cref="OverdueEscalationAfter"/> ou mais que ainda não foram
    /// levadas ao admin. Marca: o admin é avisado uma vez só e os lembretes ao tutor param.
    /// </summary>
    public IReadOnlyList<Installment> TakeOverdueEscalations(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Active or PaymentStatus.Pending))
            return [];

        var due = _installments
            .Where(i => i.Status == InstallmentStatus.Overdue
                && i.OverdueEscalatedAt is null
                && i.OverdueSince is { } since
                && now >= since + OverdueEscalationAfter)
            .ToList();

        foreach (var installment in due)
            installment.MarkOverdueEscalated(now);

        if (due.Count > 0)
            Touch(now);

        return due;
    }

    private Result Plan(int installmentCount, DateTimeOffset paymentDeadline, DateTimeOffset now)
    {
        var max = MaxInstallmentsFor(AmountCents);
        if (installmentCount < 1 || installmentCount > max)
            return Result.Failure(PaymentsErrors.InstallmentCountNotAllowed(max));

        _installments.Clear();
        _installments.AddRange(ScheduleFor(AmountCents, installmentCount, paymentDeadline, now)
            .Select(p => new Installment(p.Number, p.AmountCents, p.DueAt)));

        InstallmentCount = installmentCount;
        PlanRevision++;
        Touch(now);

        return Result.Success();
    }

    /// <summary>Divide em partes iguais; os centavos que sobram vão na 1ª.</summary>
    private static long[] Split(long amountCents, int count)
    {
        var each = amountCents / count;
        var amounts = Enumerable.Repeat(each, count).ToArray();
        amounts[0] += amountCents % count;

        return amounts;
    }

    private Installment Require(int number) =>
        FindInstallment(number) ?? throw new ArgumentOutOfRangeException(nameof(number), number, "Unknown installment.");

    /// <summary>Recalcula a situação (pendente, ativo, pago) e se tudo chegou ao fim.</summary>
    private void Touch(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Cancelled && _installments.Count > 0)
        {
            Status = _installments.All(i => i.IsPaid) ? PaymentStatus.Paid
                : _installments.Any(i => i.IsPaid) ? PaymentStatus.Active
                : PaymentStatus.Pending;
        }

        var settled = Status is PaymentStatus.Paid or PaymentStatus.Cancelled && _installments.All(i => i.IsSettled);
        SettledAt = settled ? SettledAt ?? now : null;
        UpdatedAt = now;
    }
}
