using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.ReactivateHost;

/// <summary>
/// Reativa a conta de anfitrião (e o perfil, se já aprovado) com as credenciais do login, e
/// abre a sessão. Anônimo: conta inativa não consegue entrar para pedir isso logada.
/// </summary>
public sealed record ReactivateHostCommand(string? Email, string? Password) : ICommand<HostSessionResponse>;

public sealed class ReactivateHostCommandValidator : AbstractValidator<ReactivateHostCommand>
{
    public ReactivateHostCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

/// <summary>
/// A conta vai primeiro (é ela que confere a senha e recusa conta suspensa); se o perfil não
/// puder ser gravado, a conta é inativada de novo (compensação).
/// </summary>
public sealed class ReactivateHostCommandHandler(
    IHostRepository hostRepository,
    IHostsUnitOfWork unitOfWork,
    IHostAccounts accounts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReactivateHostCommand, HostSessionResponse>
{
    public async Task<Result<HostSessionResponse>> HandleAsync(ReactivateHostCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.ReactivateAsync(command.Email, command.Password, cancellationToken).ConfigureAwait(false);
        if (account.IsFailure)
            return Result<HostSessionResponse>.FromFailure(account);

        var session = account.Value!;
        var host = await hostRepository.GetByUserIdAsync(session.User.Id, cancellationToken).ConfigureAwait(false);

        if (host is { IsActive: false })
        {
            Result saved;
            try
            {
                host.Reactivate(timeProvider.GetUtcNow());
                saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await accounts.DeactivateAsync(session.User.Id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            if (saved.IsFailure)
            {
                await accounts.DeactivateAsync(session.User.Id, CancellationToken.None).ConfigureAwait(false);
                return Result<HostSessionResponse>.FromFailure(saved);
            }

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.HostReactivated, AuditTargets.Host, host.Id.Value, ActorId: session.User.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostSessionResponse>.Success(new HostSessionResponse(
            session.AccessToken,
            session.RefreshToken,
            session.ExpiresAt,
            host is null ? null : HostResponse.From(host, session.User),
            session.User));
    }
}
