using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Sessions.CreateSession;

/// <summary>
/// Autentica o par (e-mail, role) e emite a sessão. Qualquer falha devolve o
/// mesmo <c>AUTH_INVALID_CREDENTIALS</c>: e-mail inexistente e senha errada são
/// indistinguíveis de fora.
/// </summary>
public sealed class CreateSessionCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore) : ICommandHandler<CreateSessionCommand, SessionResponse>
{
    /// <summary>
    /// Valor usado só para gastar tempo de CPU quando a conta não existe. Precisa
    /// ser não vazio porque o Argon2 recusa entrada vazia.
    /// </summary>
    private const string TimingEqualizerPassword = "timing-equalizer";

    public async Task<Result<SessionResponse>> HandleAsync(
        CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return Result<SessionResponse>.Failure(AuthErrors.InvalidCredentials);

        if (!UserRoleValues.TryParse(command.Role, out var role))
            return Result<SessionResponse>.Failure(AuthErrors.RoleUnknown);

        var user = await userRepository
            .GetByEmailAndRoleAsync(email.Value!, role, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Conta inexistente: gasta o mesmo tempo de um hash de verdade para
            // que a duração da resposta não revele se o e-mail está cadastrado.
            // O resultado é descartado.
            passwordHasher.Hash(
                string.IsNullOrEmpty(command.Password) ? TimingEqualizerPassword : command.Password);
            return Result<SessionResponse>.Failure(AuthErrors.InvalidCredentials);
        }

        if (!passwordHasher.Verify(command.Password ?? string.Empty, user.PasswordHash.Value))
            return Result<SessionResponse>.Failure(AuthErrors.InvalidCredentials);

        var accessToken = accessTokenGenerator.Generate(user);
        var refreshToken = await refreshTokenStore
            .IssueAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(
            SessionResponseFactory.Create(user, accessToken, refreshToken));
    }
}
