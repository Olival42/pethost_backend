namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>Identidade de <see cref="ReviewReport"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct ReviewReportId(Guid Value)
{
    public static ReviewReportId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
