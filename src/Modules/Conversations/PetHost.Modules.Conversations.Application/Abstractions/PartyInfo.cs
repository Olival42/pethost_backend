namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Um lado da conversa, com a conta. O id da conta nunca sai na resposta: serve para o aviso em tempo real.</summary>
/// <param name="Id">Id do tutor ou do anfitrião — nunca o da conta.</param>
/// <param name="UserId">Id da conta: para quem o hub envia os eventos.</param>
/// <param name="Name">Nome da conta; empresa mostra o nome fantasia.</param>
public sealed record PartyInfo(Guid Id, Guid UserId, string Name, string? AvatarUrl);
