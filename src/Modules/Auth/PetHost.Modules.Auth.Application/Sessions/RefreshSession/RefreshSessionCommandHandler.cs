using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Sessions.RefreshSession;

/// <summary>
/// Rotação de refresh token: o token apresentado é consumido (uso único) e um novo
/// par é emitido. Reapresentar o mesmo token falha, o que limita a janela de uso
/// de um token vazado.
/// </summary>
public sealed class RefreshSessionCommandHandler(
    IUserRepository userRepository,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore) : ICommandHandler<RefreshSessionCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result<SessionResponse>.Failure(AuthErrors.RefreshTokenInvalid);

        var userId = await refreshTokenStore
            .ConsumeAsync(command.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        if (userId is null)
            return Result<SessionResponse>.Failure(AuthErrors.RefreshTokenInvalid);

        var user = await userRepository
            .GetByIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Token válido de uma conta que não existe mais: trata como token inválido,
        // sem revelar que a conta foi removida.
        if (user is null)
            return Result<SessionResponse>.Failure(AuthErrors.RefreshTokenInvalid);

        var accessToken = accessTokenGenerator.Generate(user);
        var newRefreshToken = await refreshTokenStore
            .IssueAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(
            SessionResponseFactory.Create(user, accessToken, newRefreshToken));
    }
}
