using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Accounts;

/// <summary>
/// Contrato público do módulo Auth para criar contas (§5, porta + adaptador). Quem
/// cadastra um papel num pedido só — o módulo Owners no cadastro de tutor — usa isto
/// em vez de referenciar o Auth. A implementação fica na Infrastructure do Auth.
/// </summary>
/// <remarks>
/// Conta e perfil ficam em módulos diferentes, então não há uma transação só. O fluxo
/// esperado é: <see cref="ValidateAsync"/> (junto com a validação do próprio módulo),
/// depois <see cref="RegisterAsync"/>, e se gravar o perfil falhar,
/// <see cref="DeleteAsync"/> desfaz a conta (compensação).
/// </remarks>
public interface IAccountRegistrar
{
    /// <summary>
    /// Confere a conta sem gravar nada. Devolve <b>todos</b> os erros de validação, cada
    /// um com o campo do JSON; e-mail já usado nesse papel volta como conflito.
    /// </summary>
    Task<Result> ValidateAsync(AccountRegistration registration, CancellationToken cancellationToken);

    /// <summary>Cria a conta e já abre a sessão.</summary>
    Task<Result<AccountSession>> RegisterAsync(AccountRegistration registration, CancellationToken cancellationToken);

    /// <summary>
    /// Compensação: apaga a conta recém-criada e derruba a sessão dela. Só para quem
    /// acabou de criar a conta e não conseguiu concluir o cadastro.
    /// </summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Dados da conta base, comuns a tutor e anfitrião.</summary>
/// <param name="Role"><c>owner</c> ou <c>host</c>.</param>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>, validado como os outros campos.</param>
public sealed record AccountRegistration(
    string? FullName,
    string? Email,
    string? Password,
    string Role,
    string? Phone,
    string? BirthDate,
    AddressData? Address);

/// <summary>Conta criada e a sessão aberta para ela.</summary>
/// <param name="ExpiresAt">Expiração do access token em Unix time (segundos).</param>
public sealed record AccountSession(
    string AccessToken,
    string RefreshToken,
    long ExpiresAt,
    AccountSummary User);
