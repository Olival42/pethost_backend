using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Users.RegisterAccount;

/// <summary>
/// Cadastro da conta base, comum a tutor e anfitrião. É o passo 1: o perfil do papel
/// (CPF do tutor, cantinho do anfitrião) vem depois, já logado, no módulo do papel.
/// </summary>
/// <param name="Role"><c>owner</c> ou <c>host</c>.</param>
/// <param name="BirthDate">
/// Texto no formato <c>yyyy-MM-dd</c>. Precisa ter 18 anos ou mais. É texto, e não
/// <c>DateOnly</c>, de propósito: data inválida ou vazia vira erro de validação no
/// campo, junto com os outros, em vez de derrubar a leitura do corpo inteiro.
/// </param>
public sealed record RegisterAccountCommand(
    string? FullName,
    string? Email,
    string? Password,
    string? Role,
    string? Phone,
    string? BirthDate,
    AddressData? Address) : ICommand<SessionResponse>;
