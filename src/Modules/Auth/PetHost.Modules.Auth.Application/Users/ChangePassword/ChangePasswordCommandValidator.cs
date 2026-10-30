using FluentValidation;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Users.ChangePassword;

/// <summary>Formato antes de gastar um hash: senha atual presente e senha nova forte e diferente.</summary>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithError(AuthErrors.CurrentPasswordRequired);

        RuleFor(x => x.NewPassword)
            .StrongPassword();

        RuleFor(x => x.NewPassword)
            .Must((command, newPassword) => !string.Equals(newPassword, command.CurrentPassword, StringComparison.Ordinal))
                .WithError(AuthErrors.NewPasswordSameAsCurrent)
            .When(x => !string.IsNullOrEmpty(x.CurrentPassword));
    }
}
