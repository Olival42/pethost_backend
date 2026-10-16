namespace PetHost.Shared.Contracts.Payments;

/// <summary>
/// Contrato público de leitura do módulo Payments (§5). O Bookings pergunta por aqui se o tutor
/// está devendo, sem referenciar o Payments.
/// </summary>
public interface IPaymentDirectory
{
    /// <summary>
    /// O tutor <paramref name="ownerId"/> (<c>owner.owners.id</c>) tem alguma parcela em atraso
    /// (venceu depois do check-in e não foi paga)? Tutor assim não pede estadia nova até pagar.
    /// </summary>
    Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken);
}
