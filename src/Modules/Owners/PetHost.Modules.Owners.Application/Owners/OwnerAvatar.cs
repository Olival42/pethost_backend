using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners;

/// <summary>
/// Grava a foto na conta do tutor: a parte do Owners no fluxo de
/// <see cref="ImageReplacement"/>. A foto é da conta (Auth), então passa pelo editor de perfil.
/// </summary>
internal static class OwnerAvatar
{
    /// <summary>Troca a foto (<c>null</c> tira). Devolve o tutor e a foto anterior, para apagar.</summary>
    public static async Task<Result<ImageChange<OwnerResponse>>> SetAsync(
        Owner owner,
        string? avatarUrl,
        IOwnerAccounts ownerAccounts,
        CancellationToken cancellationToken)
    {
        // No patch de perfil, texto vazio limpa a foto.
        var patch = new AccountProfilePatch(FullName: null, Phone: null, AvatarUrl: avatarUrl ?? string.Empty, Address: null);

        var updated = await ownerAccounts
            .UpdateProfileAsync(owner.UserId, patch, cancellationToken)
            .ConfigureAwait(false);

        if (updated.IsFailure)
            return Result<ImageChange<OwnerResponse>>.FromFailure(updated);

        // O editor devolve o perfil anterior; sem foto, ela vem como texto vazio.
        var previous = updated.Value!.AvatarUrl is { Length: > 0 } url && url != avatarUrl ? url : null;

        var response = await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<OwnerResponse>>.Success(new ImageChange<OwnerResponse>(response, previous));
    }
}
