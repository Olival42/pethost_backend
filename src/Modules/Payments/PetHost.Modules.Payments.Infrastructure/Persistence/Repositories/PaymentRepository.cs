using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Payments.Domain.Payments;

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IPaymentRepository"/> sobre o EF Core.</summary>
internal sealed class PaymentRepository(PaymentsDbContext dbContext) : IPaymentRepository
{
    public Task<Payment?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken) =>
        dbContext.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId, cancellationToken);

    public Task<Payment?> GetByIntentAsync(string paymentIntentId, CancellationToken cancellationToken) =>
        dbContext.Payments.FirstOrDefaultAsync(p => p.Installments.Any(i => i.StripePaymentIntentId == paymentIntentId), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListUnsettledBookingIdsAsync(int limit, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.SettledAt == null)
            .OrderBy(p => p.UpdatedAt)
            .Select(p => p.BookingId)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<(IReadOnlyList<Payment> Items, long TotalCount)> SearchPaidByHostAsync(
        Guid hostId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Payments
            .AsNoTracking()
            .Where(p => p.HostId == hostId && p.Installments.Any(i => i.Status == InstallmentStatus.Paid));

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }

    public async Task<IReadOnlyList<Payment>> ListPaidByHostAsync(Guid hostId, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.HostId == hostId && p.Installments.Any(i => i.Status == InstallmentStatus.Paid))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Payment>> ListNeedingAttentionAsync(CancellationToken cancellationToken) =>
        await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Installments.Any(i =>
                i.Status == InstallmentStatus.Overdue
                || i.DisputeStatus == DisputeStatus.Open
                || i.DisputeStatus == DisputeStatus.Lost
                || i.RefundStatus == RefundStatus.Failed
                || (i.PayoutFailureCode != null && (i.PayoutStatus == PayoutStatus.Held || i.PayoutStatus == PayoutStatus.Transferred))))
            .OrderBy(p => p.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        dbContext.Payments
            .AsNoTracking()
            .AnyAsync(p => p.OwnerId == ownerId && p.Installments.Any(i => i.Status == InstallmentStatus.Overdue), cancellationToken);

    public void Add(Payment payment) => dbContext.Payments.Add(payment);
}
