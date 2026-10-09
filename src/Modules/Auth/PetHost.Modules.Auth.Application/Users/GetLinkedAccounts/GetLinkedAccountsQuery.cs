using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Users.GetLinkedAccounts;

/// <summary>
/// As contas da mesma pessoa (mesmo e-mail), para o seletor "Tutor / Anfitrião".
/// Inclui a conta atual.
/// </summary>
public sealed record GetLinkedAccountsQuery(Guid UserId) : IQuery<IReadOnlyList<LinkedAccountResponse>>;

/// <param name="Role"><c>owner</c> ou <c>host</c>.</param>
/// <param name="IsCurrent">É a conta do access token.</param>
public sealed record LinkedAccountResponse(string Role, bool IsActive, bool IsCurrent);
