using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reports.GetReviewReportById;

/// <summary>Uma denúncia de avaliação, para o admin.</summary>
public sealed record GetReviewReportByIdQuery(Guid ReportId) : IQuery<ReviewReportResponse>;
