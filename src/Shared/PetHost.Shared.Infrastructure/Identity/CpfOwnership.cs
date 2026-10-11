using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Identity;

namespace PetHost.Shared.Infrastructure.Identity;

/// <summary>
/// <see cref="ICpfOwnership"/>: junta as contas que usam o CPF em todos os módulos
/// (<see cref="ICpfHolderLookup"/>) e confere se alguma é de outro e-mail.
/// </summary>
/// <remarks>
/// A própria conta e as outras contas do mesmo e-mail (tutor e anfitrião da mesma pessoa)
/// não contam. Conta que sumiu do Auth também não. Uma consulta por módulo e uma ao Auth.
/// </remarks>
internal sealed class CpfOwnership(
    IEnumerable<ICpfHolderLookup> lookups,
    IUserDirectory userDirectory) : ICpfOwnership
{
    public async Task<bool> IsHeldByAnotherPersonAsync(string cpf, string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);

        var holders = await FindHoldersAsync(cpf, exceptAccountId: null, cancellationToken).ConfigureAwait(false);

        return await AnyWithOtherEmailAsync(holders, email, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsHeldByAnotherPersonAsync(string cpf, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);

        var holders = await FindHoldersAsync(cpf, accountId, cancellationToken).ConfigureAwait(false);
        if (holders.Count == 0)
            return false;

        var requester = await userDirectory.GetByIdsAsync([accountId], cancellationToken).ConfigureAwait(false);

        // Sem a conta de quem pede não há e-mail para comparar: qualquer outro dono conta.
        return requester.Count == 0
            || await AnyWithOtherEmailAsync(holders, requester[0].Email, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<Guid>> FindHoldersAsync(string cpf, Guid? exceptAccountId, CancellationToken cancellationToken)
    {
        var holders = new HashSet<Guid>();

        foreach (var lookup in lookups)
            holders.UnionWith(await lookup.FindAccountIdsAsync(cpf, cancellationToken).ConfigureAwait(false));

        if (exceptAccountId is { } except)
            holders.Remove(except);

        return holders;
    }

    private async Task<bool> AnyWithOtherEmailAsync(HashSet<Guid> holders, string? email, CancellationToken cancellationToken)
    {
        if (holders.Count == 0)
            return false;

        var accounts = await userDirectory.GetByIdsAsync(holders, cancellationToken).ConfigureAwait(false);
        var normalized = email?.Trim() ?? string.Empty;

        return accounts.Any(account => !string.Equals(account.Email, normalized, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Registro da regra do CPF entre papéis.</summary>
public static class CpfOwnershipServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="ICpfOwnership"/>. Cada módulo que guarda CPF registra o próprio
    /// <see cref="ICpfHolderLookup"/>.
    /// </summary>
    public static IServiceCollection AddCpfOwnership(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICpfOwnership, CpfOwnership>();
        return services;
    }
}
