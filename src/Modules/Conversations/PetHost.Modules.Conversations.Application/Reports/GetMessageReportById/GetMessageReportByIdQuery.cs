using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Reports.GetMessageReportById;

/// <summary>Uma denúncia, com a mensagem e as anteriores a ela (o contexto). Só admin.</summary>
/// <param name="ReportId">Vem da rota.</param>
public sealed record GetMessageReportByIdQuery(Guid ReportId) : IQuery<MessageReportResponse>;
