using FluentValidation;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Sessions.RevokeSession;

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithError(AuthErrors.RefreshTokenRequired);
    }
}
