using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="StateCode"/> ↔ <c>char(2)</c>. <c>null</c> não passa pelo conversor.
/// Valor inválido no banco estoura (§6).
/// </summary>
internal sealed class StateCodeConverter : ValueConverter<StateCode, string>
{
    public StateCodeConverter()
        : base(state => state.Value, value => FromDatabase(value))
    {
    }

    private static StateCode FromDatabase(string value)
    {
        var state = StateCode.Create(value);

        return state.IsSuccess
            ? state.Value!
            : throw new InvalidOperationException(
                $"The stored state is not valid: '{state.FirstError?.Code}'. Database integrity issue.");
    }
}
