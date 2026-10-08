namespace PetHost.Modules.Auth.Application.Sessions.Responses;

/// <summary>
/// Dados do usuário que acompanham a sessão. Nunca inclui hash de senha (§15).
/// </summary>
/// <param name="Role">Papel no formato da API: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record AuthenticatedUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    string? Phone,
    string? AvatarUrl,
    string? Neighborhood,
    string? City,
    string? State);
