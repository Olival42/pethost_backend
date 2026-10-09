using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Accounts;

/// <summary>
/// Contrato público do módulo Auth para alterar o perfil de uma conta (§5, porta +
/// adaptador). Quem edita um papel num pedido só — o módulo Owners no PATCH do tutor —
/// usa isto em vez de referenciar o Auth.
/// </summary>
/// <remarks>
/// Conta e perfil do papel ficam em módulos diferentes, sem uma transação só. O fluxo
/// esperado é: <see cref="ValidateAsync"/> junto com a validação do próprio módulo,
/// depois <see cref="UpdateAsync"/>; se gravar o resto falhar, aplicar de volta o
/// perfil anterior que ele devolve desfaz a alteração (compensação).
/// </remarks>
public interface IAccountProfileEditor
{
    /// <summary>
    /// Confere o patch contra o perfil atual, sem gravar. Devolve <b>todos</b> os erros
    /// de validação, cada um com o campo do JSON.
    /// </summary>
    Task<Result> ValidateAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken);

    /// <summary>Aplica o patch. Devolve o perfil como era antes, pronto para desfazer.</summary>
    Task<Result<AccountProfilePatch>> UpdateAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken);

    /// <summary>A senha confere com a da conta? Para confirmar alteração de dado sensível.</summary>
    Task<bool> VerifyPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken);
}

/// <summary>
/// Alteração parcial do perfil da conta: <c>null</c> mantém o valor atual. Para limpar a
/// foto ou o complemento do endereço, use texto vazio.
/// </summary>
/// <param name="BirthDate">
/// Texto <c>yyyy-MM-dd</c>. A data entra na verificação de documentos: quem chama
/// decide se a troca ainda é permitida (o tutor, só até o primeiro pagamento).
/// </param>
public sealed record AccountProfilePatch(
    string? FullName,
    string? Phone,
    string? AvatarUrl,
    AddressData? Address,
    string? BirthDate = null)
{
    /// <summary>Nenhum campo enviado: não há nada a alterar na conta.</summary>
    public bool IsEmpty =>
        FullName is null && Phone is null && AvatarUrl is null && Address is null && BirthDate is null;
}
