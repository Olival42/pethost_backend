using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Owners.Application.Owners.Responses;

/// <summary>
/// Tutor completo: o perfil de tutor (CPF) junto com os dados da conta, lidos do Auth.
/// Mesmo formato para o próprio tutor e para o admin.
/// </summary>
/// <param name="Id">Id do tutor.</param>
/// <param name="UserId">Id da conta do tutor (o mesmo de <c>user.id</c>).</param>
/// <param name="Cpf">Só dígitos (<c>52998224725</c>), sem máscara. Nulo se o admin liberou o CPF.</param>
/// <param name="IsActive">Status do perfil de tutor. Anda junto com <c>user.isActive</c>.</param>
/// <param name="SuspendedAt">Quando o admin suspendeu. O motivo vem em <c>user.suspensionReason</c>.</param>
/// <param name="User">Dados da conta. Nulo só se a conta sumiu do Auth — não deveria acontecer.</param>
public sealed record OwnerResponse(
    Guid Id,
    Guid UserId,
    string? Cpf,
    bool IsActive,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset CreatedAt,
    AccountSummary? User)
{
    public static OwnerResponse From(Owner owner, AccountSummary? user)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return new OwnerResponse(owner.Id.Value, owner.UserId, owner.Cpf?.Value, owner.IsActive, owner.SuspendedAt, owner.CreatedAt, user);
    }

    /// <summary>Monta a resposta de um tutor, buscando a conta dele no Auth.</summary>
    public static async Task<OwnerResponse> LoadAsync(
        Owner owner,
        IOwnerAccounts ownerAccounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(ownerAccounts);

        var accounts = await ownerAccounts
            .GetByIdsAsync([owner.UserId], cancellationToken)
            .ConfigureAwait(false);

        return From(owner, accounts.Count > 0 ? accounts[0] : null);
    }
}
