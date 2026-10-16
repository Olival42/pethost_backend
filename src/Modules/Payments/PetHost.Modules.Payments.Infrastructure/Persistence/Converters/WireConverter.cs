using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Converters;

/// <summary>Enum ↔ texto (<c>pending</c>, <c>transferred</c>...): enum como string (§11), pelos <c>...Values</c> do domínio.</summary>
internal sealed class WireConverter<TEnum>(Expression<Func<TEnum, string>> toWire, Expression<Func<string, TEnum>> fromWire)
    : ValueConverter<TEnum, string>(toWire, fromWire)
    where TEnum : struct, Enum;
