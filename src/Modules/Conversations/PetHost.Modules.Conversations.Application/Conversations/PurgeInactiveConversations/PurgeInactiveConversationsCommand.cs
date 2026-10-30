using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;

/// <summary>
/// Uma rodada da retenção (LGPD): apaga as conversas sem mensagem há
/// <paramref name="InactivityDays"/> dias, com as mensagens e as fotos. Disparada sem usuário.
/// </summary>
/// <param name="InactivityDays">Dias sem atividade para apagar (padrão do produto: 90).</param>
/// <param name="BatchSize">Quantas conversas cada consulta traz; a rodada vai até acabar.</param>
public sealed record PurgeInactiveConversationsCommand(int InactivityDays, int BatchSize) : ICommand<PurgeInactiveConversationsResponse>;
