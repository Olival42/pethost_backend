using FluentValidation;

namespace PetHost.Modules.Pets.Application.Pets.DeactivatePet;

/// <summary>Validador trivial: toda <c>ICommand</c> tem um (§10).</summary>
public sealed class DeactivatePetCommandValidator : AbstractValidator<DeactivatePetCommand>
{
    public DeactivatePetCommandValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage("Pet id is required.");
    }
}
