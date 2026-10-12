namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Quem é o anfitrião do cantinho, para exibição. Sem e-mail, telefone nem endereço: o contato
/// só é liberado depois do pagamento (escopo, regra 7).
/// </summary>
/// <param name="Id">Id do anfitrião — nunca o da conta.</param>
/// <param name="Name">Nome da conta; empresa mostra o nome fantasia.</param>
/// <param name="IsCompany">Pessoa jurídica.</param>
/// <param name="IsAvailable">Ativo e sem suspensão.</param>
public sealed record ListingHostSummary(Guid Id, string Name, string? AvatarUrl, bool IsCompany, bool IsAvailable);
