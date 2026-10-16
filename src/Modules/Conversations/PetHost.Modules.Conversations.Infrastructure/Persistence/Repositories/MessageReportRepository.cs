using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IMessageReportRepository"/> sobre o EF Core.</summary>
internal sealed class MessageReportRepository(ConversationsDbContext dbContext) : IMessageReportRepository
{
    public Task<MessageReport?> GetByIdAsync(MessageReportId id, CancellationToken cancellationToken) =>
        dbContext.MessageReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(MessageReport report) => dbContext.MessageReports.Add(report);
}
