namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// O acréscimo de uma parcela paga com atraso (<see cref="Payment.LateFeeFor"/>): multa fixa de
/// <see cref="Payment.LateFinePercentage"/>% e juros de <see cref="Payment.MonthlyInterestPercentage"/>%
/// ao mês, simples, por dia desde o vencimento.
/// </summary>
/// <param name="DaysLate">Dias de calendário (Brasília) desde o dia do vencimento.</param>
/// <param name="FineCents">A multa, em centavos.</param>
/// <param name="InterestCents">Os juros, em centavos.</param>
public sealed record LateFee(int DaysLate, long FineCents, long InterestCents)
{
    /// <summary>Em dia (ou dentro da tolerância): nada a mais.</summary>
    public static readonly LateFee None = new(0, 0, 0);

    /// <summary>Multa + juros, em centavos.</summary>
    public long TotalCents => FineCents + InterestCents;
}
