using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.HostApplications.ApproveHostApplication;

/// <summary>Admin aprova a solicitação: o perfil de anfitrião é criado.</summary>
/// <param name="AdminId">Vem do token do admin.</param>
/// <param name="Reason">O que foi conferido. Obrigatório: vai para a nota e para a trilha.</param>
public sealed record ApproveHostApplicationCommand(Guid AdminId, Guid ApplicationId, string? Reason)
    : ICommand<HostApplicationReviewResponse>;

public sealed class ApproveHostApplicationCommandValidator : AbstractValidator<ApproveHostApplicationCommand>
{
    public ApproveHostApplicationCommandValidator() => RuleFor(x => x.Reason).AdminReason();
}
