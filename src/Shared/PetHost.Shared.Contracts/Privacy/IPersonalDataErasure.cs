namespace PetHost.Shared.Contracts.Privacy;

/// <summary>
/// Exclusão da conta a pedido do titular (LGPD, art. 18, VI). O módulo do papel (Owners, Hosts)
/// confere as regras e chama isto; cada módulo apaga ou anonimiza a <b>sua</b> parte (§5) por um
/// <see cref="IPersonalDataEraser"/>, sem um conhecer o outro.
/// </summary>
/// <remarks>
/// Fica o que a lei manda guardar ou o que o outro lado precisa: reservas e pagamentos (obrigação
/// fiscal e contestação no Stripe), avaliações publicadas (o nome sai, porque vem da conta) e a
/// trilha de auditoria. A conta (Auth) é anonimizada <b>por último</b>, pelo
/// <see cref="Accounts.IAccountStatusManager"/>: se um módulo falhar no meio, a pessoa ainda entra
/// e repete o pedido — todo eraser é idempotente.
/// </remarks>
public interface IPersonalDataErasure
{
    /// <summary>Roda o <see cref="IPersonalDataEraser"/> de cada módulo, um depois do outro.</summary>
    Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken);
}

/// <summary>
/// "Apague a sua parte desta conta" — cada módulo com dado pessoal da conta implementa (Pets: pets e
/// fotos; Listings: fotos e endereço do cantinho; Conversations: as conversas). Registrado com
/// <c>AddScoped</c>; todos são chamados.
/// </summary>
/// <remarks>
/// Idempotente: rodar de novo sobre uma conta já apagada não muda nada. Erro de infraestrutura
/// lança (o pedido volta 500 e pode ser repetido); arquivo que não sai do bucket só vai para o log.
/// </remarks>
public interface IPersonalDataEraser
{
    Task EraseAsync(ErasedAccount account, CancellationToken cancellationToken);
}

/// <summary>A conta que está sendo excluída.</summary>
/// <param name="UserId">A conta (<c>auth.users.id</c>).</param>
/// <param name="Role"><c>owner</c> ou <c>host</c> (<see cref="Authorization.Roles"/>).</param>
/// <param name="ProfileId">
/// O perfil do papel: <c>owner.owners.id</c> para o tutor, <c>host.hosts.id</c> para o anfitrião.
/// Nulo para anfitrião que ainda não foi aprovado (só tem a solicitação).
/// </param>
public sealed record ErasedAccount(Guid UserId, string Role, Guid? ProfileId);
