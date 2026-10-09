using FluentValidation;

namespace PetHost.Modules.Owners.Application.Owners.ReactivateOwner;

/// <summary>Só presença: o resto do formato é conferido pelo Auth, como no login.</summary>
public sealed class ReactivateOwnerCommandValidator : AbstractValidator<ReactivateOwnerCommand>
{
    public ReactivateOwnerCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}
