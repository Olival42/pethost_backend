namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Como falar com um dos lados fora do app (e-mail). Só para avisos; nunca sai na API.</summary>
/// <param name="ProfileId">Id do tutor ou do anfitrião.</param>
/// <param name="UserId">Id da conta: a preferência de aviso é dela.</param>
/// <param name="Name">Nome da pessoa na conta (para a saudação; não o nome fantasia).</param>
/// <param name="CanBeNotified">Perfil e conta ativos e sem suspensão: recebe aviso.</param>
public sealed record PartyContact(Guid ProfileId, Guid UserId, string Name, string Email, bool CanBeNotified);
