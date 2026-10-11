using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts;

/// <summary>
/// Grava a foto na conta do anfitrião: a parte do Hosts no fluxo de <see cref="ImageReplacement"/>.
/// A foto é da conta (Auth), então passa pelo editor de perfil — igual ao tutor.
/// </summary>
internal static class HostAvatar
{
    /// <summary>Troca a foto (<c>null</c> tira). Devolve o anfitrião e a foto anterior, para apagar.</summary>
    public static async Task<Result<ImageChange<HostResponse>>> SetAsync(
        Host host,
        string? avatarUrl,
        IHostAccounts accounts,
        CancellationToken cancellationToken)
    {
        // No patch de perfil, texto vazio limpa a foto.
        var patch = new AccountProfilePatch(FullName: null, Phone: null, AvatarUrl: avatarUrl ?? string.Empty, Address: null);

        var updated = await accounts.UpdateProfileAsync(host.UserId, patch, cancellationToken).ConfigureAwait(false);
        if (updated.IsFailure)
            return Result<ImageChange<HostResponse>>.FromFailure(updated);

        var previous = updated.Value!.AvatarUrl is { Length: > 0 } url && url != avatarUrl ? url : null;
        var response = await HostResponse.LoadAsync(host, accounts, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<HostResponse>>.Success(new ImageChange<HostResponse>(response, previous));
    }
}
