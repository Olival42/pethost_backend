using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

/// <summary><see cref="PersonType"/> ↔ texto (<c>individual</c>, <c>company</c>): enum como string (§11).</summary>
internal sealed class PersonTypeConverter : ValueConverter<PersonType, string>
{
    public PersonTypeConverter()
        : base(type => PersonTypeValues.ToWire(type), value => PersonTypeValues.FromWire(value))
    {
    }
}
