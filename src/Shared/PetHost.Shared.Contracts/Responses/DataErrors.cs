namespace PetHost.Shared.Contracts.Responses;

/// <summary>Mensagens de validação agrupadas por campo.</summary>
public record DataErrors(string Field, List<string> Messages);
