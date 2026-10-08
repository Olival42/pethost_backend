using FluentValidation;
using PetHost.Modules.Auth.Application.Abstractions;

namespace PetHost.Modules.Auth.Application.Passwords.ResetPassword;

/// <summary>
/// Roda antes do handler: senha fraca volta <c>400</c> sem tocar no token, então a
/// pessoa pode corrigir a senha e tentar de novo com o mesmo e-mail.
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.");

        RuleFor(x => x.NewPassword)
            .StrongPassword();
    }
}
