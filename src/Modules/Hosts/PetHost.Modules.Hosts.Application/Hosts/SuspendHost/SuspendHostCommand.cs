using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.Hosts.SuspendHost;

/// <summary>Admin suspende um anfitrião: perfil e conta, com motivo.</summary>
/// <param name="AdminId">Vem do token do admin.</param>
public sealed record SuspendHostCommand(Guid AdminId, Guid HostId, string? Reason) : ICommand<HostResponse>;

/// <summary>Admin tira a suspensão de um anfitrião: perfil e conta.</summary>
public sealed record LiftHostSuspensionCommand(Guid AdminId, Guid HostId) : ICommand<HostResponse>;

/// <summary>
/// Admin libera o CPF (pessoa física) ou o CNPJ e o CPF do representante (empresa) de um
/// anfitrião suspenso — disputa: alguém usou o documento de outra pessoa ou empresa. Com motivo.
/// </summary>
public sealed record ReleaseHostIdentityCommand(Guid AdminId, Guid HostId, string? Reason) : ICommand<HostResponse>;

public sealed class SuspendHostCommandValidator : AbstractValidator<SuspendHostCommand>
{
    public SuspendHostCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}

public sealed class LiftHostSuspensionCommandValidator : AbstractValidator<LiftHostSuspensionCommand>
{
    public LiftHostSuspensionCommandValidator() => RuleFor(x => x.HostId).NotEmpty().WithMessage("Host id is required.");
}

public sealed class ReleaseHostIdentityCommandValidator : AbstractValidator<ReleaseHostIdentityCommand>
{
    public ReleaseHostIdentityCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}
