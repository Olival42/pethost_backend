namespace PetHost.Shared.Contracts.Responses;

/// <summary>Estrutura padronizada de erro.</summary>
public record ErrorResponse(string Code, string Message, object? Details = null);
