using System.Collections.Concurrent;
using PetHost.Modules.Owners.Application.Abstractions;

namespace PetHost.Modules.Owners.IntegrationTests;

/// <summary>
/// Dublê da porta de pagamentos do Owners: o teste diz quais tutores "já pagaram". O caminho de
/// verdade (o Payments respondendo pelo contrato) é testado no <c>Payments.IntegrationTests</c>, que
/// tem o fluxo de pagamento inteiro.
/// </summary>
public sealed class FakeOwnerPayments : IOwnerPayments
{
    private readonly ConcurrentDictionary<Guid, bool> _paid = new();

    public void MarkPaid(Guid ownerId) => _paid[ownerId] = true;

    public void Clear() => _paid.Clear();

    public Task<bool> HasPaidAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult(_paid.ContainsKey(ownerId));
}
