using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.TestKit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports;

/// <summary>
/// A denúncia padrão dos testes: a Camila (tutora) denuncia uma mensagem que a Dona Cida (anfitriã)
/// mandou, por golpe. E as linhas que a leitura projeta para ela.
/// </summary>
internal static class ReportsTestData
{
    public static readonly DateTimeOffset ReportedAt = ConversationBuilder.DefaultNow.AddHours(1);

    public static Message HostMessage(Conversation conversation) =>
        ConversationBuilder.Text(conversation, ConversationParty.Host, "Me paga por fora no Pix que sai mais barato.");

    public static MessageReport Report(Conversation conversation, Message? message = null) =>
        MessageReport.Submit(message ?? HostMessage(conversation), ConversationParty.Owner, ReportReason.Scam, "Pediu Pix por fora.", ReportedAt).Value!;

    public static MessageRow Row(Message message) =>
        new(message.Id, message.ConversationId, message.SentBy, message.Body, message.ImageUrl, message.BookingId, message.CreatedAt);

    public static ReportRow Row(Conversation conversation, MessageReport report, Message message) =>
        new(
            report.Id,
            report.ConversationId,
            conversation.OwnerId,
            conversation.HostId,
            Row(message),
            report.ReportedBy,
            report.Reason,
            report.Description,
            report.Status,
            report.CreatedAt,
            report.ResolvedAt,
            report.ResolvedBy,
            report.ResolutionNote);
}
