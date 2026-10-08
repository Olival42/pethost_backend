using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Sessions.RevokeSession;

/// <summary>
/// Logout. Idempotente de propósito: revogar um token já inválido devolve sucesso,
/// porque o estado desejado pelo cliente — token sem valor — foi alcançado.
/// </summary>
public sealed class RevokeSessionCommandHandler(IRefreshTokenStore refreshTokenStore)
    : ICommandHandler<RevokeSessionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            await refreshTokenStore
                .RevokeAsync(command.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
