namespace PetHost.Shared.Contracts.Identity;

/// <summary>
/// Restrição 9 do escopo: <b>o CPF identifica a pessoa; o e-mail identifica o login</b>. Um
/// CPF pode aparecer numa conta de tutor e numa de anfitrião só se as duas forem do mesmo
/// e-mail (a mesma pessoa). Todo módulo que grava CPF pergunta isto antes (§5).
/// </summary>
/// <remarks>
/// Quem guarda CPF registra um <see cref="ICpfHolderLookup"/>; a implementação junta todos e
/// compara os e-mails das contas pelo contrato de leitura do Auth.
/// </remarks>
public interface ICpfOwnership
{
    /// <summary>
    /// O CPF já é de uma conta com <b>outro</b> e-mail? Para quem ainda não tem conta (o
    /// cadastro do tutor cria a conta depois desta conferência).
    /// </summary>
    /// <param name="cpf">Só dígitos, já validado.</param>
    Task<bool> IsHeldByAnotherPersonAsync(string cpf, string email, CancellationToken cancellationToken);

    /// <summary>O CPF já é de uma conta com e-mail diferente do da conta <paramref name="accountId"/>?</summary>
    Task<bool> IsHeldByAnotherPersonAsync(string cpf, Guid accountId, CancellationToken cancellationToken);
}

/// <summary>
/// "Quais contas usam este CPF aqui?" — cada módulo que guarda CPF implementa (Owners: tutores;
/// Hosts: anfitriões e solicitações abertas). Registrado com <c>AddScoped</c>; todos são lidos.
/// </summary>
public interface ICpfHolderLookup
{
    /// <summary>Ids das <b>contas</b> (users.id) que usam o CPF neste módulo.</summary>
    /// <param name="cpf">Só dígitos, já validado.</param>
    Task<IReadOnlyCollection<Guid>> FindAccountIdsAsync(string cpf, CancellationToken cancellationToken);
}
