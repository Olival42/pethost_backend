namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Situação do repasse (90%) de uma parcela ao anfitrião.</summary>
public enum PayoutStatus
{
    /// <summary>Guardado com o PetHost até o check-out (ou até a contestação ser decidida).</summary>
    Held,

    /// <summary>Transferido para a conta conectada do anfitrião.</summary>
    Transferred,

    /// <summary>Não haverá repasse: tudo foi devolvido ao tutor ou a contestação foi perdida antes do repasse.</summary>
    Cancelled,

    /// <summary>Estornado do anfitrião: a contestação foi perdida depois do repasse.</summary>
    Reversed,
}
