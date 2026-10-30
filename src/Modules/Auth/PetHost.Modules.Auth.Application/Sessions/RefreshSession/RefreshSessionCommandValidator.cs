using FluentValidation;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Sessions.RefreshSession;

public sealed class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithError(AuthErrors.RefreshTokenRequired);
    }
}
