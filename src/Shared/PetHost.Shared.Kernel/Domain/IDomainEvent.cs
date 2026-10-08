namespace PetHost.Shared.Kernel.Domain;

/// <summary>Fato consumado dentro do módulo. Para cruzar fronteira, use integration event (§5).</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
