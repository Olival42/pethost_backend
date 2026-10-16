namespace PetHost.Modules.Conversations.Domain.Reports;

/// <summary>Identidade de <see cref="MessageReport"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct MessageReportId(Guid Value)
{
    public static MessageReportId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
