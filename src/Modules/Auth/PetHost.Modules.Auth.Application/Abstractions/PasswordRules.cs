using FluentValidation;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>Regras de senha para os validadores de entrada.</summary>
public static class PasswordRules
{
    /// <summary>
    /// Senha forte, pela regra do value object <see cref="Password"/> — uma fonte só,
    /// o validador não repete a regra. Cada regra que falha vira uma mensagem no campo
    /// validado, então o cliente recebe a lista do que falta.
    /// </summary>
    public static IRuleBuilderOptionsConditions<T, string?> StrongPassword<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder.Custom((value, context) =>
        {
            var password = Password.Create(value);
            if (password.IsSuccess)
                return;

            foreach (var error in password.Errors!)
                context.AddFailure(error.Message);
        });
}
