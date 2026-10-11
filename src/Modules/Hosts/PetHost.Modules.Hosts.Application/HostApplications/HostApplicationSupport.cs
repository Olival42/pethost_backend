using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Identity;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.HostApplications;

/// <summary>Passos repetidos entre os casos de uso da solicitação.</summary>
internal static class HostApplicationSupport
{
    /// <summary>A solicitação aberta da conta, ou <see cref="HostsErrors.OpenApplicationNotFound"/>.</summary>
    public static async Task<Result<HostApplication>> GetOpenAsync(
        IHostApplicationRepository repository,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var history = await repository.ListByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var open = history.FirstOrDefault(a => a.IsOpen);

        return open is null
            ? Result<HostApplication>.Failure(HostsErrors.OpenApplicationNotFound)
            : Result<HostApplication>.Success(open);
    }

    /// <summary>
    /// O CPF é desta pessoa? Recusa CPF já usado por uma conta de <b>outro e-mail</b> — tutor,
    /// anfitrião ou solicitação aberta (restrição 9). O tutor da mesma pessoa não conta.
    /// </summary>
    public static async Task<Result> EnsureCpfAvailableAsync(
        Cpf cpf,
        Guid userId,
        ICpfOwnership cpfOwnership,
        CancellationToken cancellationToken)
    {
        return await cpfOwnership.IsHeldByAnotherPersonAsync(cpf.Value, userId, cancellationToken).ConfigureAwait(false)
            ? Result.Failure(HostsErrors.CpfAlreadyRegistered)
            : Result.Success();
    }

    /// <summary>O CNPJ está livre? Recusa CNPJ de anfitrião ou de solicitação aberta de outra conta.</summary>
    public static async Task<Result> EnsureCnpjAvailableAsync(
        Cnpj cnpj,
        Guid userId,
        IHostRepository hostRepository,
        IHostApplicationRepository applicationRepository,
        CancellationToken cancellationToken)
    {
        if (await hostRepository.ExistsByCnpjAsync(cnpj, cancellationToken).ConfigureAwait(false))
            return Result.Failure(HostsErrors.CnpjAlreadyRegistered);

        if (await applicationRepository.ExistsOpenWithCnpjAsync(cnpj, userId, cancellationToken).ConfigureAwait(false))
            return Result.Failure(HostsErrors.CnpjAlreadyRegistered);

        return Result.Success();
    }

    /// <summary>
    /// Apaga as fotos da solicitação fechada se o prazo de guarda já passou (com prazo zero,
    /// na hora). Grava primeiro e só então apaga do bucket: se gravar falhar, as fotos ficam
    /// e a tarefa de limpeza tenta de novo.
    /// </summary>
    /// <returns>Quantas fotos foram apagadas.</returns>
    public static async Task<int> PurgeDocumentsIfDueAsync(
        HostApplication application,
        IHostVerificationPolicy policy,
        IPrivateImageStorage storage,
        IHostsUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (application.IsOpen || application.ClosedAt is not { } closedAt || closedAt + policy.DocumentRetention > now)
            return 0;

        var keys = application.PurgeDocuments(now);
        if (keys.Count == 0)
            return 0;

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return 0;

        foreach (var key in keys)
            await storage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);

        return keys.Count;
    }

    /// <summary>A conta da solicitação, para a resposta do admin e para o aviso por e-mail.</summary>
    public static async Task<Shared.Contracts.Accounts.AccountSummary?> GetAccountAsync(
        IHostAccounts accounts,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var found = await accounts.GetByIdsAsync([userId], cancellationToken).ConfigureAwait(false);
        return found.Count > 0 ? found[0] : null;
    }
}
