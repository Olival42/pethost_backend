using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reminders.SendReviewReminders;

/// <summary>
/// Rodada da tarefa em segundo plano: lembra por e-mail cada lado que ainda não avaliou uma estadia
/// concluída há <paramref name="DelayHours"/> horas, enquanto o prazo de 7 dias não acaba. Um
/// lembrete por lado por reserva.
/// </summary>
/// <param name="DelayHours">Quanto esperar depois do check-out antes de lembrar.</param>
/// <param name="BatchSize">Quantas estadias cada rodada olha.</param>
public sealed record SendReviewRemindersCommand(int DelayHours, int BatchSize) : ICommand<SendReviewRemindersResponse>;

/// <summary>Quantos lembretes foram para a caixa de saída.</summary>
public sealed record SendReviewRemindersResponse(int EmailsSent);
