namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>Quem é o tutor ou o anfitrião, para exibição. Sem e-mail, telefone, endereço nem id da conta.</summary>
/// <param name="Id">Id do tutor ou do anfitrião — nunca o da conta.</param>
/// <param name="Name">Nome da conta; empresa mostra o nome fantasia.</param>
public sealed record PartySummary(Guid Id, string Name, string? AvatarUrl);
