using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="ReportReason"/> ↔ texto (<c>spam</c>, <c>harassment</c>...): enum como string (§11).</summary>
internal sealed class ReportReasonConverter : ValueConverter<ReportReason, string>
{
    public ReportReasonConverter()
        : base(reason => ReportReasonValues.ToWire(reason), value => ReportReasonValues.FromWire(value))
    {
    }
}
