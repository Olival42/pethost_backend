namespace PetHost.Modules.Pets.Application.Abstractions;

/// <summary>
/// Quem é o dono do pet, para exibição. Sem e-mail, telefone nem endereço: o contato só é
/// liberado depois do pagamento (escopo, regra 7).
/// </summary>
/// <param name="Id">Id do tutor ou do anfitrião — nunca o da conta.</param>
/// <param name="Type"><c>owner</c> ou <c>host</c>.</param>
/// <param name="Name">Nome da conta; anfitrião empresa mostra o nome fantasia.</param>
public sealed record KeeperSummary(Guid Id, string Type, string Name, string? AvatarUrl);
