namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>O tutor ou o anfitrião da conta logada.</summary>
/// <param name="Id">Id do tutor (<c>owner.owners.id</c>) ou do anfitrião (<c>host.hosts.id</c>), nunca o da conta.</param>
/// <param name="IsAvailable">Ativo e sem suspensão: pode puxar conversa e enviar mensagem.</param>
public sealed record PartyProfile(Guid Id, bool IsAvailable);
