using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

/// <summary><see cref="ReportStatus"/> ↔ texto (<c>pending</c>, <c>upheld</c>, <c>dismissed</c>): enum como string (§11).</summary>
internal sealed class ReportStatusConverter : ValueConverter<ReportStatus, string>
{
    public ReportStatusConverter()
        : base(status => ReportStatusValues.ToWire(status), value => ReportStatusValues.FromWire(value))
    {
    }
}
