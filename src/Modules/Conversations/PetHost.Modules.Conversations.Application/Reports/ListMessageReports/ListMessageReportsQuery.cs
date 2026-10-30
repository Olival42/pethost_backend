using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Reports.ListMessageReports;

/// <summary>A fila de denúncias do admin.</summary>
/// <param name="Status"><c>pending</c>, <c>upheld</c> ou <c>dismissed</c>; nulo = todas.</param>
/// <param name="Page">Começa em 1.</param>
/// <param name="PageSize">De 1 a 100.</param>
public sealed record ListMessageReportsQuery(
    string? Status,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<MessageReportResponse>>;
