namespace PetHost.Modules.Owners.Application.Abstractions;

/// <summary>
/// Porta para os pagamentos (módulo Payments). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IOwnerPayments
{
    /// <summary>O tutor já pagou alguma parcela? Marco da trava de CPF e data de nascimento.</summary>
    Task<bool> HasPaidAsync(Guid ownerId, CancellationToken cancellationToken);
}
