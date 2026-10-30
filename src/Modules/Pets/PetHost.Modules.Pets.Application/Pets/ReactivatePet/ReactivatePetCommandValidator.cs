using FluentValidation;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Pets.Application.Pets.ReactivatePet;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class ReactivatePetCommandValidator : AbstractValidator<ReactivatePetCommand>
{
    public ReactivatePetCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithError(PetsErrors.PetIdRequired);
    }
}
