using FluentValidation;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users.RegisterAccount;

/// <summary>
/// Valida a conta inteira de uma vez, usando as regras dos próprios value objects.
/// A idade mínima também é checada aqui, para vir junto com os outros erros em vez
/// de só depois que todo o resto estiver certo.
/// </summary>
public sealed class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
    public RegisterAccountCommandValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(x => x.FullName).MustBeValid(FullName.Create);
        RuleFor(x => x.Email).MustBeValid(Email.Create);
        RuleFor(x => x.Password).StrongPassword();

        RuleFor(x => x.Role)
            .Must(role => UserRoleValues.TryParse(role, out var parsed) && parsed is UserRole.Owner or UserRole.Host)
                .WithMessage("Role must be either 'owner' or 'host'.");

        RuleFor(x => x.Phone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Phone is required.")
            .MustBeValid(PhoneNumber.Create);

        RuleFor(x => x.BirthDate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(AuthErrors.BirthDateRequired.Message)
            .Must(text => TryParseBirthDate(text, out _))
                .WithMessage(AuthErrors.BirthDateInvalidFormat.Message)
            .Must(text => TryParseBirthDate(text, out var date)
                && date <= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
                .WithMessage(AuthErrors.BirthDateInFuture.Message)
            .Must(text => TryParseBirthDate(text, out var date)
                && User.AgeAt(date, timeProvider.GetUtcNow()) >= User.MinimumAge)
                .WithMessage(AuthErrors.Underage.Message);

        RuleFor(x => x.Address)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(AuthErrors.AddressRequired.Message)
            .MustBeValidComposite<RegisterAccountCommand, AddressData, Address>(CreateAddress);
    }

    /// <summary>Data no formato <c>yyyy-MM-dd</c>, sem depender da cultura do servidor.</summary>
    public static bool TryParseBirthDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(
            text?.Trim(),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);

    public static Result<Address> CreateAddress(AddressData address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Address.Create(
            address.ZipCode,
            address.Street,
            address.Number,
            address.Complement,
            address.Neighborhood,
            address.City,
            address.State);
    }
}
