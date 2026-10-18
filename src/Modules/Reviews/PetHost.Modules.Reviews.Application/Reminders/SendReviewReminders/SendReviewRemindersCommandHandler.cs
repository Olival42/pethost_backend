using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;

/// <summary>
/// Pega as estadias concluídas entre 7 dias atrás e <c>DelayHours</c> atrás; para cada lado (tutor e
/// anfitrião) que ainda não avaliou nem recebeu lembrete, manda o e-mail e anota. Quem não pode
/// receber aviso (perfil ou conta inativa, suspensa) fica de fora e é olhado de novo na próxima rodada.
/// </summary>
/// <remarks>
/// Os lembretes são gravados <b>antes</b> de ir para a caixa de saída: se a gravação falhar, ninguém
/// recebe e a próxima rodada tenta de novo; nunca um e-mail repetido.
/// </remarks>
public sealed class SendReviewRemindersCommandHandler(
    IReviewBookings reviewBookings,
    IReviewRepository reviewRepository,
    IReviewReminderRepository reminderRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IReviewNotifier reviewNotifier,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SendReviewRemindersCommand, SendReviewRemindersResponse>
{
    private static readonly ReviewParty[] Sides = [ReviewParty.Owner, ReviewParty.Host];

    public async Task<Result<SendReviewRemindersResponse>> HandleAsync(SendReviewRemindersCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var stays = await reviewBookings
            .ListCompletedAsync(now.AddDays(-Review.WindowDays), now.AddHours(-command.DelayHours), command.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (stays.Count == 0)
            return Result<SendReviewRemindersResponse>.Success(new SendReviewRemindersResponse(0));

        var bookingIds = stays.Select(s => s.BookingId).ToList();
        var reviewed = await reviewRepository.GetReviewedSidesAsync(bookingIds, cancellationToken).ConfigureAwait(false);
        var sent = await reminderRepository.GetSentAsync(bookingIds, cancellationToken).ConfigureAwait(false);

        var pending = stays
            .SelectMany(stay => Sides.Select(side => (Stay: stay, Side: side)))
            .Where(p => !reviewed.Contains((p.Stay.BookingId, p.Side)) && !sent.Contains((p.Stay.BookingId, p.Side)))
            .ToList();

        if (pending.Count == 0)
            return Result<SendReviewRemindersResponse>.Success(new SendReviewRemindersResponse(0));

        var owners = await ContactsAsync(ReviewParty.Owner, pending, cancellationToken).ConfigureAwait(false);
        var hosts = await ContactsAsync(ReviewParty.Host, pending, cancellationToken).ConfigureAwait(false);

        var toSend = new List<(ReviewedStay Stay, ReviewParty Side, ReviewContact Contact)>();
        foreach (var (stay, side) in pending)
        {
            var contacts = side == ReviewParty.Owner ? owners : hosts;
            var profileId = side == ReviewParty.Owner ? stay.OwnerId : stay.HostId;

            if (contacts.GetValueOrDefault(profileId) is { IsReachable: true } contact)
            {
                toSend.Add((stay, side, contact));
                reminderRepository.Add(new ReviewReminder(stay.BookingId, side, now));
            }
        }

        if (toSend.Count == 0)
            return Result<SendReviewRemindersResponse>.Success(new SendReviewRemindersResponse(0));

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<SendReviewRemindersResponse>.FromFailure(saved);

        foreach (var (stay, side, contact) in toSend)
        {
            var deadline = stay.CheckedOutAt.GetValueOrDefault().AddDays(Review.WindowDays);
            await reviewNotifier.SendReminderAsync(contact, side, deadline, cancellationToken).ConfigureAwait(false);
            await ReviewTrail.RecordReminderSentAsync(auditTrail, stay.BookingId, side, cancellationToken).ConfigureAwait(false);
        }

        return Result<SendReviewRemindersResponse>.Success(new SendReviewRemindersResponse(toSend.Count));
    }

    private async Task<IReadOnlyDictionary<Guid, ReviewContact>> ContactsAsync(
        ReviewParty side,
        List<(ReviewedStay Stay, ReviewParty Side)> pending,
        CancellationToken cancellationToken)
    {
        var ids = pending
            .Where(p => p.Side == side)
            .Select(p => side == ReviewParty.Owner ? p.Stay.OwnerId : p.Stay.HostId)
            .Distinct()
            .ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, ReviewContact>()
            : await reviewParties.GetContactsAsync(side, ids, cancellationToken).ConfigureAwait(false);
    }
}
