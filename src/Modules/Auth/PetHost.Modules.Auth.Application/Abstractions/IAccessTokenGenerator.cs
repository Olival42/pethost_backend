using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>Porta de emissão do access token (JWT).</summary>
public interface IAccessTokenGenerator
{
    /// <summary>Emite um JWT com as claims de identidade e papel do usuário.</summary>
    AccessToken Generate(User user);
}
