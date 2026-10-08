using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Passwords.ResetPassword;

/// <summary>
/// Consome o token (uso único), grava o hash da senha nova e derruba todas as
/// sessões da conta. Quem estava logado com a senha antiga — inclusive quem a
/// roubou — precisa entrar de novo.
/// </summary>
public sealed class ResetPasswordCommandHandler(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IPasswordResetTokenStore passwordResetTokenStore,
    IRefreshTokenStore refreshTokenStore,
    TimeProvider timeProvider) : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result<Unit>.Failure(AuthErrors.PasswordResetTokenInvalid);

        // O validador já barrou senha fraca com 400; aqui o value object é a garantia
        // de que nenhum caminho grava hash de senha fora da regra.
        var newPassword = Password.Create(command.NewPassword);
        if (newPassword.IsFailure)
            return Result<Unit>.FromFailure(newPassword);

        var userId = await passwordResetTokenStore
            .ConsumeAsync(command.Token, cancellationToken)
            .ConfigureAwait(false);

        if (userId is null)
            return Result<Unit>.Failure(AuthErrors.PasswordResetTokenInvalid);

        var user = await userRepository
            .GetByIdAsync(userId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Token válido de uma conta que não existe mais: mesma resposta de token
        // inválido, sem revelar que a conta foi removida.
        if (user is null)
            return Result<Unit>.Failure(AuthErrors.PasswordResetTokenInvalid);

        var passwordHash = PasswordHash.FromHash(passwordHasher.Hash(newPassword.Value!.Value));
        if (passwordHash.IsFailure)
            return Result<Unit>.FromFailure(passwordHash);

        var changed = user.ChangePassword(passwordHash.Value!, timeProvider.GetUtcNow());
        if (changed.IsFailure)
            return Result<Unit>.FromFailure(changed);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await refreshTokenStore
            .RevokeAllAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit>.Success(Unit.Value);
    }
}
