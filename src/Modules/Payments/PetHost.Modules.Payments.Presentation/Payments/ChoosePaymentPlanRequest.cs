namespace PetHost.Modules.Payments.Presentation.Payments;

/// <summary>Corpo da escolha do parcelamento.</summary>
/// <param name="InstallmentCount">1 (à vista) a 6; cada parcela de pelo menos R$ 50,00.</param>
public sealed record ChoosePaymentPlanRequest(int InstallmentCount);
