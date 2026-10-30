using System.Globalization;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Users.RegisterAccount;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Application.Users;

/// <summary>Perfil da conta depois de aplicado um <see cref="AccountProfilePatch"/>.</summary>
/// <param name="BirthDate"><c>null</c> quando a data não veio no patch: não muda.</param>
public sealed record ProfileChanges(
    FullName FullName,
    PhoneNumber Phone,
    AvatarUrl? AvatarUrl,
    Address Address,
    DateOnly? BirthDate);

/// <summary>
/// Aplica um <see cref="AccountProfilePatch"/> sobre o perfil atual, sem gravar. Usado pelo
/// contrato de edição de perfil que o módulo de um papel (Owners) chama.
/// </summary>
public static class ProfilePatch
{
    private const string BirthDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// O que não veio mantém o valor atual; o endereço é mesclado campo a campo. Texto
    /// vazio limpa a foto e o complemento. Devolve <b>todos</b> os erros de uma vez.
    /// </summary>
    public static Result<ProfileChanges> Apply(User user, AccountProfilePatch patch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(patch);

        var errors = new List<Error>();

        var fullName = patch.FullName is null ? user.FullName : Collect(FullName.Create(patch.FullName), errors);

        var phone = patch.Phone is null ? user.Phone : Collect(PhoneNumber.Create(patch.Phone), errors);
        if (phone is null && patch.Phone is null)
            errors.Add(AuthErrors.PhoneRequired);

        var avatarUrl = patch.AvatarUrl switch
        {
            null => user.AvatarUrl,
            _ when string.IsNullOrWhiteSpace(patch.AvatarUrl) => null,
            _ => Collect(AvatarUrl.Create(patch.AvatarUrl), errors),
        };

        var address = patch.Address is null
            ? user.Address
            : Collect(RegisterAccountCommandValidator.CreateAddress(Merge(user.Address, patch.Address)), errors);
        if (address is null && patch.Address is null)
            errors.Add(AuthErrors.AddressRequired);

        DateOnly? birthDate = null;
        if (patch.BirthDate is not null)
        {
            if (string.IsNullOrWhiteSpace(patch.BirthDate))
                errors.Add(AuthErrors.BirthDateRequired);
            else if (!RegisterAccountCommandValidator.TryParseBirthDate(patch.BirthDate, out var parsed))
                errors.Add(AuthErrors.BirthDateInvalidFormat);
            else if (User.ValidateBirthDate(parsed, now) is { } birthDateError)
                errors.Add(birthDateError);
            else
                birthDate = parsed;
        }

        return errors.Count > 0
            ? Result<ProfileChanges>.Failure(errors)
            : Result<ProfileChanges>.Success(new ProfileChanges(fullName!, phone!, avatarUrl, address!, birthDate));
    }

    /// <summary>
    /// Perfil atual no formato do patch. Opcional ausente vira texto vazio, para que
    /// aplicar o snapshot de volta também desfaça o que o patch preencheu.
    /// </summary>
    public static AccountProfilePatch Snapshot(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var address = AddressMapping.ToAddressData(user.Address);

        return new AccountProfilePatch(
            user.FullName.Value,
            user.Phone?.Value,
            user.AvatarUrl?.Value ?? string.Empty,
            address is null ? null : address with { Complement = address.Complement ?? string.Empty },
            user.BirthDate?.ToString(BirthDateFormat, CultureInfo.InvariantCulture));
    }

    private static AddressData Merge(Address? current, AddressData patch)
    {
        var data = AddressMapping.ToAddressData(current);

        return new AddressData(
            patch.ZipCode ?? data?.ZipCode,
            patch.Street ?? data?.Street,
            patch.Number ?? data?.Number,
            patch.Complement ?? data?.Complement,
            patch.Neighborhood ?? data?.Neighborhood,
            patch.City ?? data?.City,
            patch.State ?? data?.State);
    }

    private static T? Collect<T>(Result<T> result, List<Error> errors)
        where T : class
    {
        if (result.IsSuccess)
            return result.Value;

        errors.AddRange(result.Errors!);
        return null;
    }
}
