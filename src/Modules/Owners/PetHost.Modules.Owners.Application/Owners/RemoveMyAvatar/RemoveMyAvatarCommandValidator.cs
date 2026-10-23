using FluentValidation;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemoveMyAvatarCommandValidator : AbstractValidator<RemoveMyAvatarCommand>
{
    public RemoveMyAvatarCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithError(OwnersErrors.UserIdRequired);
    }
}
