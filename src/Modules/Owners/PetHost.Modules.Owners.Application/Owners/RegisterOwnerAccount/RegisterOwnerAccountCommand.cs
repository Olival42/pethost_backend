using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;

/// <summary>
/// Cadastro de tutor num pedido só: a conta (Auth) e o perfil de tutor (Owners).
/// O papel é sempre <c>owner</c>, por isso não vem na request.
/// </summary>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>. Precisa ter 18 anos ou mais.</param>
/// <param name="Cpf">Com ou sem máscara.</param>
public sealed record RegisterOwnerAccountCommand(
    string? FullName,
    string? Email,
    string? Password,
    string? Phone,
    string? BirthDate,
    string? Cpf,
    AddressData? Address) : ICommand<OwnerSessionResponse>
{
    public AccountRegistration ToAccountRegistration() =>
        new(FullName, Email, Password, Roles.Owner, Phone, BirthDate, Address);
}
