using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;

/// <summary>
/// A nota média e a contagem de <b>um</b> anfitrião, cantinho, tutor ou pet (exatamente um). A do
/// tutor e a do pet só saem pelas rotas de anfitrião/admin.
/// </summary>
public sealed record GetRatingSummaryQuery(
    Guid? HostId = null,
    Guid? ListingId = null,
    Guid? OwnerId = null,
    Guid? PetId = null) : IQuery<RatingSummaryResponse>;
