using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;

/// <summary>
/// Uma rodada do e-mail de "mensagens não lidas": avisa quem tem mensagem esperando há
/// <paramref name="DelayMinutes"/> minutos. Disparada sem usuário, pela tarefa em segundo plano.
/// </summary>
/// <param name="DelayMinutes">Quanto tempo a mensagem fica sem ler antes do e-mail (padrão: 15).</param>
/// <param name="CooldownMinutes">Intervalo mínimo entre dois e-mails da mesma conversa para a mesma pessoa (padrão: 60).</param>
/// <param name="BatchSize">Quantas conversas, de cada lado, a rodada olha; o resto fica para a próxima.</param>
public sealed record NotifyUnreadMessagesCommand(int DelayMinutes, int CooldownMinutes, int BatchSize)
    : ICommand<NotifyUnreadMessagesResponse>;

/// <param name="EmailsSent">Quantos e-mails foram para a caixa de saída (um por pessoa, com todas as conversas dela).</param>
public sealed record NotifyUnreadMessagesResponse(int EmailsSent);
