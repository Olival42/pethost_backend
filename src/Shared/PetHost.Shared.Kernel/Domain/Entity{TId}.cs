namespace PetHost.Shared.Kernel.Domain;

/// <summary>Raiz de agregado: identidade própria e fila de domain events.</summary>
public abstract class Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(TId id) => Id = id;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
