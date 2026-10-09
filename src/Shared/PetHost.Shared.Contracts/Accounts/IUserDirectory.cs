namespace PetHost.Shared.Contracts.Accounts;

/// <summary>
/// Contrato público de leitura do módulo Auth (§5, consulta síncrona por porta +
/// adaptador). Outro módulo que precisa mostrar dados da conta — nome, e-mail,
/// telefone — usa isto em vez de referenciar o Auth ou fazer JOIN entre schemas.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Contas pelos ids, numa consulta só. Ids que não existem simplesmente não
    /// aparecem no resultado.
    /// </summary>
    Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

/// <summary>Dados da conta para exibição. Nunca inclui hash de senha (§15).</summary>
/// <param name="SuspendedAt">Quando o admin suspendeu a conta. Nulo se não está suspensa.</param>
/// <param name="SuspensionReason">Motivo da suspensão, informado pelo admin.</param>
public sealed record AccountSummary(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    string? Phone,
    string? AvatarUrl,
    DateOnly? BirthDate,
    AddressData? Address,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SuspendedAt = null,
    string? SuspensionReason = null);
