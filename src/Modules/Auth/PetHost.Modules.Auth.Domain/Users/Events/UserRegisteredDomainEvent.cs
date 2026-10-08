using PetHost.Shared.Kernel.Domain;

namespace PetHost.Modules.Auth.Domain.Users.Events;

/// <summary>
/// Conta criada. Fica dentro do módulo Auth; para cruzar fronteira seria
/// preciso um integration event em <c>Shared.Contracts</c> via outbox (§5).
/// </summary>
public sealed record UserRegisteredDomainEvent(UserId UserId, UserRole Role, DateTimeOffset OccurredAt)
    : IDomainEvent;
