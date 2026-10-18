namespace PetHost.Shared.Contracts.Payments;

/// <summary>
/// Contrato público de leitura do módulo Payments (§5). O Bookings pergunta por aqui se o tutor
/// está devendo; o Hosts, quanto o anfitrião recebeu (painel) — sem referenciar o Payments.
/// </summary>
public interface IPaymentDirectory
{
    /// <summary>
    /// O tutor <paramref name="ownerId"/> (<c>owner.owners.id</c>) tem alguma parcela em atraso
    /// (venceu depois do check-in e não foi paga)? Tutor assim não pede estadia nova até pagar.
    /// </summary>
    Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// O tutor <paramref name="ownerId"/> (<c>owner.owners.id</c>) já pagou alguma parcela (mesmo que
    /// depois reembolsada)? É o marco do "primeiro pagamento": daí em diante o Owners trava o CPF e a
    /// data de nascimento.
    /// </summary>
    Task<bool> HasPaidAnyInstallmentAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Os ganhos do anfitrião <paramref name="hostId"/> (<c>host.hosts.id</c>) no mês de
    /// <paramref name="month"/> (qualquer dia do mês; o mês é contado no horário de Brasília), no mês
    /// anterior e no ano até o fim do mês, mais o que está guardado e o que ainda vai entrar agora.
    /// </summary>
    Task<HostEarningsSummary> GetHostEarningsAsync(Guid hostId, DateOnly month, CancellationToken cancellationToken);
}

/// <summary>Ganhos do anfitrião, em centavos (como o Stripe).</summary>
/// <param name="TransferredCents">Repassado no mês: o que foi transferido (data do repasse no mês), antes de estornos.</param>
/// <param name="ReversedCents">Estornado no mês (contestação perdida depois do repasse; data do estorno no mês).</param>
/// <param name="PreviousMonthTransferredCents">Repassado no mês anterior, antes de estornos.</param>
/// <param name="YearToDateTransferredCents">Repassado de 1º de janeiro até o fim do mês, antes de estornos.</param>
/// <param name="Breakdown">Como se chega no repassado do mês, a partir das parcelas repassadas nele.</param>
/// <param name="Held">Guardado agora, separado pelo motivo. Não depende do mês.</param>
/// <param name="Expected">O que ainda vai entrar das parcelas que os tutores não pagaram. Não depende do mês.</param>
public sealed record HostEarningsSummary(
    long TransferredCents,
    long ReversedCents,
    long PreviousMonthTransferredCents,
    long YearToDateTransferredCents,
    HostEarningsBreakdown Breakdown,
    HostHeldPayouts Held,
    HostExpectedPayouts Expected);

/// <summary>
/// As parcelas repassadas no mês: <c>PaidByOwners − Refunded − PlatformFee = repassado</c>
/// (a regra do dicionário: <c>platform_fee + host_payout = amount + late_fee − refunded</c>).
/// </summary>
/// <param name="PaidByOwnersCents">O que os tutores pagaram nessas parcelas, multa e juros incluídos.</param>
/// <param name="LateFeeCents">A parte de multa e juros por atraso (já dentro de <paramref name="PaidByOwnersCents"/>).</param>
/// <param name="RefundedCents">Devolvido ao tutor nessas parcelas (cancelamento com reembolso parcial).</param>
/// <param name="PlatformFeeCents">Os 10% do PetHost (mais o centavo do arredondamento).</param>
public sealed record HostEarningsBreakdown(
    long PaidByOwnersCents,
    long LateFeeCents,
    long RefundedCents,
    long PlatformFeeCents);

/// <summary>Parcelas pagas ainda não repassadas: os 90% previstos de cada uma, pelo motivo.</summary>
/// <param name="AwaitingCheckOutCents">A estadia ainda não acabou: o repasse sai no check-out.</param>
/// <param name="DisputedCents">Contestação aberta no cartão: nada se move até o Stripe decidir.</param>
/// <param name="BlockedCents">
/// O repasse não passou — o Stripe bloqueou os repasses do anfitrião ou recusou a transferência. A
/// manutenção tenta de novo.
/// </param>
public sealed record HostHeldPayouts(long AwaitingCheckOutCents, long DisputedCents, long BlockedCents)
{
    public long TotalCents => AwaitingCheckOutCents + DisputedCents + BlockedCents;
}

/// <summary>Parcelas ainda por pagar nas reservas com pagamento em andamento.</summary>
/// <param name="Cents">Os 90% previstos do valor das parcelas (sem multa por atraso, que ainda não existe).</param>
/// <param name="Installments">Quantas parcelas.</param>
public sealed record HostExpectedPayouts(long Cents, int Installments);
