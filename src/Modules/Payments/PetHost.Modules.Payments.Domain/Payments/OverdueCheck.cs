namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Resultado de <see cref="Payment.CheckOverdue"/>.</summary>
/// <param name="MissedBeforeCheckIn">
/// Uma parcela venceu antes do check-in e passou da tolerância: a reserva deve ser cancelada pela
/// plataforma (com 100% de reembolso).
/// </param>
/// <param name="BecameOverdue">Parcelas que acabaram de entrar em atraso (a estadia já começou).</param>
public sealed record OverdueCheck(bool MissedBeforeCheckIn, IReadOnlyList<int> BecameOverdue)
{
    public static OverdueCheck None { get; } = new(false, []);
}
