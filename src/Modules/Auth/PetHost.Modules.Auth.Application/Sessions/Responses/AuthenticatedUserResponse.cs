using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Auth.Application.Sessions.Responses;

/// <summary>
/// Dados do usuário que acompanham a sessão. Nunca inclui hash de senha (§15).
/// São os dados da própria pessoa, então o endereço vai completo.
/// </summary>
/// <param name="Role">Papel no formato da API: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record AuthenticatedUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    string? Phone,
    string? AvatarUrl,
    DateOnly? BirthDate,
    AddressData? Address,
    bool IsActive);
