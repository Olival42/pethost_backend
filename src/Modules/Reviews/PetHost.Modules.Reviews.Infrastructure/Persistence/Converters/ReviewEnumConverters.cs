using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Converters;

/// <summary><see cref="ReviewParty"/> ↔ texto (<c>owner</c>, <c>host</c>): enum como string (§11).</summary>
internal sealed class ReviewPartyConverter : ValueConverter<ReviewParty, string>
{
    public ReviewPartyConverter()
        : base(party => ReviewPartyValues.ToWire(party), value => ReviewPartyValues.FromWire(value))
    {
    }
}

/// <summary><see cref="ReviewReportReason"/> ↔ texto.</summary>
internal sealed class ReviewReportReasonConverter : ValueConverter<ReviewReportReason, string>
{
    public ReviewReportReasonConverter()
        : base(reason => ReviewReportReasonValues.ToWire(reason), value => ReviewReportReasonValues.FromWire(value))
    {
    }
}

/// <summary><see cref="ReviewReportStatus"/> ↔ texto.</summary>
internal sealed class ReviewReportStatusConverter : ValueConverter<ReviewReportStatus, string>
{
    public ReviewReportStatusConverter()
        : base(status => ReviewReportStatusValues.ToWire(status), value => ReviewReportStatusValues.FromWire(value))
    {
    }
}

/// <summary><see cref="ReviewReportId"/> ↔ <c>uuid</c>.</summary>
internal sealed class ReviewReportIdConverter : ValueConverter<ReviewReportId, Guid>
{
    public ReviewReportIdConverter()
        : base(id => id.Value, value => new ReviewReportId(value))
    {
    }
}
