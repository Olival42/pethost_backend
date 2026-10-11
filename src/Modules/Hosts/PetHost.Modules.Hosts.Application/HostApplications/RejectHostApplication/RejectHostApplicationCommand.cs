using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;

/// <summary>Admin recusa a solicitação.</summary>
/// <param name="AdminId">Vem do token do admin.</param>
/// <param name="RejectionReason">Motivo que a pessoa vê (<c>document_illegible</c>...). <c>suspected_fraud</c> é definitivo.</param>
/// <param name="Reason">Nota interna do admin. Obrigatória: vai para a trilha.</param>
public sealed record RejectHostApplicationCommand(Guid AdminId, Guid ApplicationId, string? RejectionReason, string? Reason)
    : ICommand<HostApplicationReviewResponse>;

public sealed class RejectHostApplicationCommandValidator : AbstractValidator<RejectHostApplicationCommand>
{
    public RejectHostApplicationCommandValidator()
    {
        RuleFor(x => x.RejectionReason)
            .Must(reason => RejectionReasonValues.TryParse(reason, out _)).WithMessage(HostsErrors.RejectionReasonInvalid.Message);
        RuleFor(x => x.Reason).AdminReason();
    }
}
