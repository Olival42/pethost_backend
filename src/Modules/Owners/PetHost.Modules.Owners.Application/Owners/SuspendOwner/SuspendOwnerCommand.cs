using FluentValidation;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.SuspendOwner;

/// <summary>Admin suspende um tutor: perfil e conta, com motivo.</summary>
/// <param name="AdminId">Vem do token do admin.</param>
public sealed record SuspendOwnerCommand(Guid AdminId, Guid OwnerId, string? Reason) : ICommand<OwnerResponse>;

/// <summary>Admin tira a suspensão de um tutor: perfil e conta.</summary>
public sealed record LiftOwnerSuspensionCommand(Guid AdminId, Guid OwnerId) : ICommand<OwnerResponse>;

/// <summary>Admin libera o CPF de um tutor suspenso (disputa de CPF), com motivo.</summary>
public sealed record ReleaseOwnerCpfCommand(Guid AdminId, Guid OwnerId, string? Reason) : ICommand<OwnerResponse>;

/// <summary>Toda ação do admin sobre o tutor de outra pessoa leva motivo — ele vai para a trilha.</summary>
public sealed class SuspendOwnerCommandValidator : AbstractValidator<SuspendOwnerCommand>
{
    public SuspendOwnerCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}

public sealed class ReleaseOwnerCpfCommandValidator : AbstractValidator<ReleaseOwnerCpfCommand>
{
    public ReleaseOwnerCpfCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}

internal static class AdminReasonRules
{
    public static IRuleBuilderOptions<T, string?> AdminReason<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(OwnersErrors.ReasonRequired.Message)
            .Must(reason => reason!.Trim().Length <= Owner.AdminReasonMaxLength).WithMessage(OwnersErrors.ReasonTooLong.Message);
}
