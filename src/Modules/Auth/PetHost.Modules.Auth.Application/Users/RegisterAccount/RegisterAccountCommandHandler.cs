using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.RegisterAccount;

/// <summary>
/// Cria a conta base e emite a sessão. Confere unicidade no par (e-mail, role),
/// monta os value objects e delega a criação ao agregado (§9).
/// </summary>
public sealed class RegisterAccountCommandHandler(
    IUserRepository userRepository,
    IAuthUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenGenerator accessTokenGenerator,
    IRefreshTokenStore refreshTokenStore,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RegisterAccountCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken)
    {
        // O validador já barrou entrada inválida; os value objects são a garantia
        // de que nenhum caminho cria conta fora da regra.
        var fullName = FullName.Create(command.FullName);
        var email = Email.Create(command.Email);
        var password = Password.Create(command.Password);
        var phone = PhoneNumber.Create(command.Phone);
        var address = command.Address is null
            ? Result<Address>.Failure(AuthErrors.AddressRequired)
            : RegisterAccountCommandValidator.CreateAddress(command.Address);

        var invalid = new Result[] { fullName, email, password, phone, address }.FirstOrDefault(r => r.IsFailure);
        if (invalid is not null)
            return Result<SessionResponse>.FromFailure(invalid);

        if (!UserRoleValues.TryParse(command.Role, out var role))
            return Result<SessionResponse>.Failure(AuthErrors.RoleInvalid);

        if (!RegisterAccountCommandValidator.TryParseBirthDate(command.BirthDate, out var birthDate))
            return Result<SessionResponse>.Failure(AuthErrors.BirthDateInvalidFormat);

        var alreadyRegistered = await userRepository
            .ExistsByEmailAndRoleAsync(email.Value!, role, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyRegistered)
            return Result<SessionResponse>.Failure(AuthErrors.EmailAlreadyRegistered);

        var passwordHash = PasswordHash.FromHash(passwordHasher.Hash(password.Value!.Value));
        if (passwordHash.IsFailure)
            return Result<SessionResponse>.FromFailure(passwordHash);

        var user = User.Register(
            fullName.Value!,
            email.Value!,
            passwordHash.Value!,
            role,
            phone.Value!,
            birthDate,
            address.Value!,
            timeProvider.GetUtcNow());

        if (user.IsFailure)
            return Result<SessionResponse>.FromFailure(user);

        userRepository.Add(user.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.AccountRegistered,
                    AuditTargets.Account,
                    user.Value!.Id.Value,
                    Details: new Dictionary<string, string?> { ["role"] = UserRoleValues.ToWire(role) },
                    ActorId: user.Value!.Id.Value),
                cancellationToken)
            .ConfigureAwait(false);

        var accessToken = accessTokenGenerator.Generate(user.Value!);
        var refreshToken = await refreshTokenStore
            .IssueAsync(user.Value!.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result<SessionResponse>.Success(
            SessionResponseFactory.Create(user.Value!, accessToken, refreshToken));
    }
}
