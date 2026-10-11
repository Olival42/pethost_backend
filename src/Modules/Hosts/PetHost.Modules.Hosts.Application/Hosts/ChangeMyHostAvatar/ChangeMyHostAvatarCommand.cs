using FluentValidation;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.ChangeMyHostAvatar;

/// <summary>Troca a foto de perfil do anfitrião logado pela imagem enviada.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record ChangeMyHostAvatarCommand(Guid UserId, ImageUpload? Image) : ICommand<HostResponse>;

/// <summary>Tira a foto de perfil do anfitrião logado.</summary>
public sealed record RemoveMyHostAvatarCommand(Guid UserId) : ICommand<HostResponse>;

/// <summary>Só a conta: o arquivo é conferido pelo bucket, depois de saber que o perfil existe.</summary>
public sealed class ChangeMyHostAvatarCommandValidator : AbstractValidator<ChangeMyHostAvatarCommand>
{
    public ChangeMyHostAvatarCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

public sealed class RemoveMyHostAvatarCommandValidator : AbstractValidator<RemoveMyHostAvatarCommand>
{
    public RemoveMyHostAvatarCommandValidator() => RuleFor(x => x.UserId).NotEmpty().WithMessage("User id is required.");
}

/// <summary>
/// Envia a imagem ao bucket público, grava a URL na conta (<c>user.avatarUrl</c>, no Auth) e
/// apaga a anterior — o mesmo fluxo do tutor (<see cref="ImageReplacement"/>).
/// </summary>
public sealed class ChangeMyHostAvatarCommandHandler(
    IHostRepository hostRepository,
    IHostAccounts accounts,
    IImageStorage imageStorage) : ICommandHandler<ChangeMyHostAvatarCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(ChangeMyHostAvatarCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.ProfileNotFound);

        return await ImageReplacement
            .ReplaceAsync(imageStorage, command.Image, ImageFolders.Avatars, (image, ct) => HostAvatar.SetAsync(host, image.Url, accounts, ct), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Tira a foto e apaga a imagem do bucket. Idempotente.</summary>
public sealed class RemoveMyHostAvatarCommandHandler(
    IHostRepository hostRepository,
    IHostAccounts accounts,
    IImageStorage imageStorage) : ICommandHandler<RemoveMyHostAvatarCommand, HostResponse>
{
    public async Task<Result<HostResponse>> HandleAsync(RemoveMyHostAvatarCommand command, CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostResponse>.Failure(HostsErrors.ProfileNotFound);

        return await ImageReplacement
            .RemoveAsync(imageStorage, ct => HostAvatar.SetAsync(host, null, accounts, ct), cancellationToken)
            .ConfigureAwait(false);
    }
}
