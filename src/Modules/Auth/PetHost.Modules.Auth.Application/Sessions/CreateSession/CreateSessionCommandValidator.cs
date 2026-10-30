using FluentValidation;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Validation;

namespace PetHost.Modules.Auth.Application.Sessions.CreateSession;

/// <summary>
/// Valida só o formato. Não diz se o e-mail existe nem se a senha está certa —
/// isso é resposta única e genérica do handler, para não virar oráculo de contas.
/// </summary>
public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithError(AuthErrors.EmailRequired)
            .MaximumLength(Email.MaxLength)
                .WithError(AuthErrors.EmailTooLong);

        RuleFor(x => x.Password)
            .NotEmpty().WithError(AuthErrors.PasswordRequired)
            .MaximumLength(Password.MaxLength)
                .WithError(AuthErrors.PasswordTooLong);

        RuleFor(x => x.Role)
            .NotEmpty().WithError(AuthErrors.RoleRequired)
            .Must(role => UserRoleValues.TryParse(role, out _))
                .WithError(AuthErrors.RoleUnknown);
    }
}
