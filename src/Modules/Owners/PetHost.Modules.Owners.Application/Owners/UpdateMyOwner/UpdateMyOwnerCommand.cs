using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;

/// <summary>
/// Altera o tutor logado num pedido só (PATCH): dados da conta e CPF. Só muda o que
/// vier; <c>null</c> mantém o valor atual.
/// </summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>. Fica na conta (Auth), mas só muda até o primeiro pagamento.</param>
/// <param name="CurrentPassword">Obrigatória só quando o CPF ou a data de nascimento mudam.</param>
public sealed record UpdateMyOwnerCommand(
    Guid UserId,
    string? FullName,
    string? Phone,
    AddressData? Address,
    string? BirthDate,
    string? Cpf,
    string? CurrentPassword) : ICommand<OwnerResponse>
{
    /// <summary>A foto não muda por aqui: tem rota própria (<c>PUT</c>/<c>DELETE /owners/me/avatar</c>).</summary>
    public AccountProfilePatch ToProfilePatch() => new(FullName, Phone, AvatarUrl: null, Address, BirthDate);
}
