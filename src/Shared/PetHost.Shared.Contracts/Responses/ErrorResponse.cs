namespace PetHost.Shared.Contracts.Responses;

/// <summary>Estrutura padronizada de erro. <c>Params</c> leva os valores que a frase cita (datas, limites, ids), nos erros de topo.</summary>
public record ErrorResponse(string Code, string Message, object? Details = null, IReadOnlyDictionary<string, object>? Params = null);
