namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Em que pé está a reserva, no que importa para o pagamento.</summary>
public enum BookingStage
{
    /// <summary>Solicitada: o anfitrião ainda não aceitou. Nada a pagar ainda.</summary>
    Requested,

    /// <summary>Aceita: esperando a 1ª parcela.</summary>
    AwaitingPayment,

    /// <summary>Confirmada, antes do check-in: o dinheiro fica guardado.</summary>
    Confirmed,

    /// <summary>Check-in feito: o pet está com o anfitrião.</summary>
    InProgress,

    /// <summary>Check-out feito: libera o repasse do que já foi pago.</summary>
    Completed,

    /// <summary>Cancelada pelo tutor, pelo anfitrião ou pela plataforma: devolve conforme o percentual.</summary>
    Cancelled,

    /// <summary>Expirada, recusada ou inexistente: nada deveria ter sido pago — o que foi pago volta inteiro.</summary>
    Closed,
}
