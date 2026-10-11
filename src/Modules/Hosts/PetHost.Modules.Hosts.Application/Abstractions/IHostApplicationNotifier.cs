namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>Avisa a pessoa da decisão do admin (e-mail, em segundo plano).</summary>
public interface IHostApplicationNotifier
{
    Task NotifyApprovedAsync(HostApplicationDecision decision, CancellationToken cancellationToken);

    Task NotifyRejectedAsync(HostApplicationDecision decision, CancellationToken cancellationToken);
}

/// <summary>O que vai no aviso. Sem CPF nem nota interna do admin.</summary>
/// <param name="RejectionReason">Motivo da recusa (<c>document_illegible</c>...). Nulo na aprovação.</param>
/// <param name="CanTryAgain">Na recusa: a pessoa pode abrir outra solicitação?</param>
public sealed record HostApplicationDecision(
    string Email,
    string FullName,
    string? RejectionReason = null,
    bool CanTryAgain = false);
