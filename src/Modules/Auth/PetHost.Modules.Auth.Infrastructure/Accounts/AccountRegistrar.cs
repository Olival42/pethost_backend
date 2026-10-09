using FluentValidation;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Users.RegisterAccount;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Infrastructure.Validation;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Infrastructure.Accounts;

/// <summary>
/// Adaptador do contrato <see cref="IAccountRegistrar"/> (§5): a porta pela qual outro
/// módulo cria conta. Por dentro, usa o mesmo validador e o mesmo handler do cadastro.
/// </summary>
internal sealed class AccountRegistrar(
    IEnumerable<IValidator<RegisterAccountCommand>> validators,
    ICommandHandler<RegisterAccountCommand, SessionResponse> registerAccount,
    IUserDirectory userDirectory,
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IRefreshTokenStore refreshTokenStore,
    IAccessTokenRevocationStore accessTokenRevocationStore,
    IAuditTrail auditTrail) : IAccountRegistrar
{
    public async Task<Result> ValidateAsync(AccountRegistration registration, CancellationToken cancellationToken)
    {
        var command = ToCommand(registration);
        var errors = new List<Error>();

        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
            errors.AddRange(validation.Errors.Select(failure =>
                Error.Validation(ValidationFieldNames.ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
        }

        if (errors.Count > 0)
            return Result.Failure(errors);

        // Formato ok: confere se o e-mail já tem conta nesse papel (409, não 400).
        var email = Email.Create(command.Email);
        if (email.IsFailure || !UserRoleValues.TryParse(command.Role, out var role))
            return Result.Success();

        var taken = await userRepository
            .ExistsByEmailAndRoleAsync(email.Value!, role, cancellationToken)
            .ConfigureAwait(false);

        return taken ? Result.Failure(AuthErrors.EmailAlreadyRegistered) : Result.Success();
    }

    public async Task<Result<AccountSession>> RegisterAsync(
        AccountRegistration registration,
        CancellationToken cancellationToken)
    {
        var session = await registerAccount
            .HandleAsync(ToCommand(registration), cancellationToken)
            .ConfigureAwait(false);

        if (session.IsFailure)
            return Result<AccountSession>.FromFailure(session);

        // O resumo da conta sai do mesmo contrato de leitura que os outros módulos usam.
        var accounts = await userDirectory
            .GetByIdsAsync([session.Value!.User.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<AccountSession>.Success(new AccountSession(
            session.Value.AccessToken,
            session.Value.RefreshToken,
            session.Value.ExpiresAt,
            accounts[0]));
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var id = new UserId(userId);
        var user = await userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (user is not null)
        {
            userRepository.Remove(user);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(
                    new AuditRecord(
                        AuditActions.AccountDeleted,
                        AuditTargets.Account,
                        userId,
                        Details: new Dictionary<string, string?> { ["cause"] = "registration_rolled_back" }),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await refreshTokenStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);
        await accessTokenRevocationStore.RevokeAllAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private static RegisterAccountCommand ToCommand(AccountRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return new RegisterAccountCommand(
            registration.FullName,
            registration.Email,
            registration.Password,
            registration.Role,
            registration.Phone,
            registration.BirthDate,
            registration.Address);
    }
}
