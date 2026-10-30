using FluentValidation;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;

/// <summary>
/// Só a conta. O arquivo (faltando, grande, formato) é conferido pelo bucket, depois de
/// saber que a conta tem perfil de tutor.
/// </summary>
public sealed class ChangeMyAvatarCommandValidator : AbstractValidator<ChangeMyAvatarCommand>
{
    public ChangeMyAvatarCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithError(OwnersErrors.UserIdRequired);
    }
}
