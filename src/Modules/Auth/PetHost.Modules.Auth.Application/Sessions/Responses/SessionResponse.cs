namespace PetHost.Modules.Auth.Application.Sessions.Responses;

/// <summary>
/// Sessão autenticada: o par de tokens, quando o access token expira e quem é o usuário.
/// Retorno de cadastro, login e refresh — o cliente trata os três igual.
/// </summary>
/// <param name="ExpiresAt">
/// Instante em que o access token expira, em Unix time (segundos desde 1970-01-01 UTC) —
/// o mesmo valor do claim <c>exp</c> do JWT. É o campo que o cliente usa para agendar o refresh.
/// </param>
public sealed record SessionResponse(
    string AccessToken,
    string RefreshToken,
    long ExpiresAt,
    AuthenticatedUserResponse User);
