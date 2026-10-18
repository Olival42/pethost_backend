using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications.ReleaseIdentityBlock;

/// <summary>
/// O admin libera um CPF bloqueado para solicitação de anfitrião. Para quando o bloqueio pegou o
/// dono de verdade (alguém usou o CPF dele e foi recusado por fraude), depois de o suporte conferir
/// o documento. O bloqueio da <b>conta</b> que foi recusada continua.
/// </summary>
/// <param name="AdminId">Vem do token do admin.</param>
/// <param name="Cpf">Com ou sem máscara. Só é usado para calcular a impressão; não é gravado.</param>
/// <param name="Reason">Obrigatório: vai para a trilha.</param>
public sealed record ReleaseIdentityBlockCommand(Guid AdminId, string? Cpf, string? Reason) : ICommand<Unit>;

public sealed class ReleaseIdentityBlockCommandValidator : AbstractValidator<ReleaseIdentityBlockCommand>
{
    public ReleaseIdentityBlockCommandValidator()
    {
        RuleFor(x => x.Cpf).ValidCpf();
        RuleFor(x => x.Reason).AdminReason();
    }
}

/// <summary>CPF que não está bloqueado: <c>404 HOST_IDENTITY_BLOCK_NOT_FOUND</c>. A trilha guarda o CPF mascarado.</summary>
public sealed class ReleaseIdentityBlockCommandHandler(
    IIdentityBlockRepository identityBlocks,
    ICpfFingerprint fingerprint,
    IHostsUnitOfWork unitOfWork,
    IAuditTrail auditTrail) : ICommandHandler<ReleaseIdentityBlockCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReleaseIdentityBlockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cpf = HostApplicationRules.ToCpf(command.Cpf);
        var block = await identityBlocks.FindAsync(fingerprint.Of(cpf), cancellationToken).ConfigureAwait(false);
        if (block is null)
            return Result<Unit>.Failure(HostsErrors.IdentityBlockNotFound);

        identityBlocks.Remove(block);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<Unit>.FromFailure(saved);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.HostIdentityUnblocked,
                    AuditTargets.HostApplication,
                    block.ApplicationId,
                    command.Reason,
                    new Dictionary<string, string?>
                    {
                        ["cpf"] = cpf.ToString(),
                        ["blockReason"] = IdentityBlockReasonValues.ToWire(block.Reason),
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit>.Success(Unit.Value);
    }
}
