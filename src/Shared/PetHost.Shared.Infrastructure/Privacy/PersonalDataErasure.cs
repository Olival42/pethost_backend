using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Contracts.Privacy;

namespace PetHost.Shared.Infrastructure.Privacy;

/// <summary>
/// <see cref="IPersonalDataErasure"/>: chama o <see cref="IPersonalDataEraser"/> de cada módulo, em
/// sequência (os módulos dividem o request e o <c>DbContext</c> de cada um não é thread-safe).
/// </summary>
/// <remarks>
/// O primeiro que lançar interrompe a rodada: os erasers são idempotentes, então repetir o pedido
/// refaz os que já rodaram sem mudar nada e completa os que faltaram.
/// </remarks>
internal sealed class PersonalDataErasure(IEnumerable<IPersonalDataEraser> erasers) : IPersonalDataErasure
{
    public async Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        foreach (var eraser in erasers)
            await eraser.EraseAsync(account, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registro da exclusão de conta entre módulos.</summary>
public static class PersonalDataErasureServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IPersonalDataErasure"/>. Cada módulo com dado pessoal registra o próprio
    /// <see cref="IPersonalDataEraser"/>.
    /// </summary>
    public static IServiceCollection AddPersonalDataErasure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPersonalDataErasure, PersonalDataErasure>();
        return services;
    }
}
