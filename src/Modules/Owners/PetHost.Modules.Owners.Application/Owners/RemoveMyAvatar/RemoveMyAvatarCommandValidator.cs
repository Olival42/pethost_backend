using FluentValidation;

namespace PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class RemoveMyAvatarCommandValidator : AbstractValidator<RemoveMyAvatarCommand>
{
    public RemoveMyAvatarCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");
    }
}
