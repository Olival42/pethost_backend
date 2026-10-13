namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Uma forma de parcelar o valor da reserva, para o tutor escolher.</summary>
/// <param name="Count">Número de parcelas (1 = à vista).</param>
/// <param name="FirstAmountCents">Valor da 1ª parcela (leva os centavos que sobram da divisão).</param>
/// <param name="OtherAmountCents">Valor de cada uma das outras (igual à 1ª quando divide certinho).</param>
public sealed record InstallmentOption(int Count, long FirstAmountCents, long OtherAmountCents);
