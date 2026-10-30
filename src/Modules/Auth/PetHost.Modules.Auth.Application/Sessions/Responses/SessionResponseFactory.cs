using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Sessions.Responses;

/// <summary>
/// Monta o <see cref="SessionResponse"/> em um só lugar. Cadastro, login e refresh
/// devolvem exatamente o mesmo formato porque passam por aqui.
/// </summary>
internal static class SessionResponseFactory
{
    public static SessionResponse Create(User user, AccessToken accessToken, RefreshToken refreshToken) =>
        new(
            accessToken.Value,
            refreshToken.Value,
            accessToken.ExpiresAt.ToUnixTimeSeconds(),
            ToUserResponse(user));

    public static AuthenticatedUserResponse ToUserResponse(User user) =>
        new(
            user.Id.Value,
            user.FullName.Value,
            user.Email.Value,
            UserRoleValues.ToWire(user.Role),
            user.Phone?.Value,
            user.AvatarUrl?.Value,
            user.BirthDate,
            AddressMapping.ToAddressData(user.Address),
            user.IsActive);
}
