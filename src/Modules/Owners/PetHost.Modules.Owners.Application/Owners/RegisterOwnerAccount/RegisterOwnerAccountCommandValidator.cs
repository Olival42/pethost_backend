using FluentValidation;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;

/// <summary>
/// Valida o cadastro inteiro de uma vez: o CPF aqui, e a conta (nome, e-mail, senha
/// forte, telefone, nascimento, endereço) pela porta do Auth. Todos os erros de formato
/// voltam juntos num só 400.
/// </summary>
public sealed class RegisterOwnerAccountCommandValidator : AbstractValidator<RegisterOwnerAccountCommand>
{
    public RegisterOwnerAccountCommandValidator(IOwnerAccounts ownerAccounts)
    {
        ArgumentNullException.ThrowIfNull(ownerAccounts);

        RuleFor(x => x.Cpf)
            .Must(cpf => Cpf.Create(cpf).IsSuccess)
                .WithMessage(OwnersErrors.CpfInvalid.Message);

        RuleFor(x => x).CustomAsync(async (command, context, cancellationToken) =>
        {
            var account = await ownerAccounts
                .ValidateAsync(command.ToAccountRegistration(), cancellationToken)
                .ConfigureAwait(false);

            if (account.IsSuccess)
                return;

            // Só erros de formato. "E-mail já usado" é 409 e fica com o handler.
            foreach (var error in account.Errors!.Where(e => e.Code == ErrorCodes.Validation))
                context.AddFailure(error.Field ?? string.Empty, error.Message);
        });
    }
}
