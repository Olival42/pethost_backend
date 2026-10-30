namespace PetHost.Modules.Bookings.Infrastructure.Expiration;

/// <summary>
/// Tarefa que expira os pedidos sem resposta. Vem do ambiente como
/// <c>BookingExpiration__IntervalMinutes</c> etc.
/// </summary>
public sealed class BookingExpirationOptions
{
    public const string SectionName = "BookingExpiration";

    /// <summary>Liga a tarefa em segundo plano. Os testes desligam e chamam o comando direto.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a tarefa roda.</summary>
    public int IntervalMinutes { get; init; } = 5;
}
