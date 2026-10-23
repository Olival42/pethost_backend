using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Contracts.Responses;

/// <summary>
/// Falhas de validação de um campo. <paramref name="Errors"/> corresponde a <paramref name="Messages"/>
/// item a item (mesma ordem, mesmo tamanho): a mensagem fica para log e compatibilidade, o código
/// estável é o que o front traduz.
/// </summary>
public record DataErrors(string Field, List<string> Messages, List<FieldErrorItem> Errors);

/// <summary>Motivo estável de uma regra de validação que falhou, com os parâmetros que ela usa.</summary>
/// <param name="Code">Ex.: <c>BOOKING_CHECK_IN_TOO_SOON</c>.</param>
/// <param name="Params">Limites e formatos da regra (nunca o valor enviado). Ausente quando não há.</param>
public record FieldErrorItem(string Code, IReadOnlyDictionary<string, object>? Params = null)
{
    public static FieldErrorItem From(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Reason is null)
            throw new InvalidOperationException($"The validation error for '{error.Field}' has no reason code: '{error.Message}'.");

        return new FieldErrorItem(error.Reason, error.Params?.Values);
    }
}
