using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para os ganhos do anfitrião (módulo Payments). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IHostEarnings
{
    /// <summary>
    /// Repassado e estornado no mês de <paramref name="month"/> (horário de Brasília), repassado no
    /// mês anterior e guardado agora, em centavos.
    /// </summary>
    Task<HostEarningsSummary> GetAsync(Guid hostId, DateOnly month, CancellationToken cancellationToken);
}
