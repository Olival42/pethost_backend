namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>Identidade de <see cref="Review"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct ReviewId(Guid Value)
{
    public static ReviewId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
