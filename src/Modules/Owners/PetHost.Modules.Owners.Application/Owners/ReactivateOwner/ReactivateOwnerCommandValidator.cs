using FluentValidation;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Owners.Application.Owners.ReactivateOwner;

/// <summary>Só presença: o resto do formato é conferido pelo Auth, como no login.</summary>
public sealed class ReactivateOwnerCommandValidator : AbstractValidator<ReactivateOwnerCommand>
{
    public ReactivateOwnerCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithError(OwnersErrors.EmailRequired);
        RuleFor(x => x.Password).NotEmpty().WithError(OwnersErrors.PasswordRequired);
    }
}
