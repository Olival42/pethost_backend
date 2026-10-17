namespace PetHost.Shared.Contracts.Bookings;

/// <summary>
/// Contrato público de leitura do módulo Bookings (§5). Outros módulos perguntam por aqui o que
/// depende das reservas — o Pets (pet com estadia em aberto; anfitrião que recebeu o pet), o
/// Listings (vaga no período; selos), o Conversations (estadia em andamento da dupla; reserva em
/// aberto que segura a retenção) e o Payments (status, valor e prazo da reserva) — sem
/// referenciar o Bookings nem fazer JOIN entre schemas.
/// </summary>
public interface IBookingDirectory
{
    /// <summary>
    /// O pet está em alguma reserva em aberto (solicitada, aceita, confirmada ou em andamento)?
    /// Pet assim não pode ser desativado.
    /// </summary>
    Task<bool> HasOpenBookingForPetAsync(Guid petId, CancellationToken cancellationToken);

    /// <summary>
    /// O tutor <paramref name="ownerId"/> (<c>owner.owners.id</c>) tem alguma reserva em aberto
    /// (solicitada, aceita, confirmada ou em andamento)? Tutor assim não pode inativar o perfil.
    /// </summary>
    Task<bool> HasOpenBookingForOwnerAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// O anfitrião <paramref name="hostId"/> (<c>host.hosts.id</c>) tem alguma reserva em aberto
    /// (solicitada, aceita, confirmada ou em andamento)? Anfitrião assim não pode inativar o perfil.
    /// </summary>
    Task<bool> HasOpenBookingForHostAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// O cantinho <paramref name="listingId"/> (<c>listing.listings.id</c>) tem alguma reserva em aberto
    /// (solicitada, aceita, confirmada ou em andamento)? Cantinho assim não pode ser inativado.
    /// </summary>
    Task<bool> HasOpenBookingForListingAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>
    /// Ids das reservas em aberto (solicitada, aceita, confirmada ou em andamento) dos cantinhos do
    /// anfitrião <paramref name="hostId"/>, da chegada mais próxima para a mais distante. É o que o
    /// admin precisa conferir quando o Stripe bloqueia os repasses do anfitrião.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListOpenBookingIdsForHostAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// O pet foi pedido para um cantinho do anfitrião <paramref name="hostId"/> numa reserva
    /// ainda válida ou já concluída (solicitada, aceita, confirmada, em andamento ou concluída)?
    /// É o que deixa o anfitrião ler a ficha completa do pet. Recusada, expirada ou cancelada não conta.
    /// </summary>
    Task<bool> IsPetBookedWithHostAsync(Guid petId, Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Para cada cantinho com reservas que ocupam vaga (aceita, confirmada ou em andamento) entre
    /// <paramref name="checkIn"/> e <paramref name="checkOut"/>: o maior número de pets numa mesma
    /// noite do período. Cantinho vazio no período não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPeakOccupancyAsync(
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken);

    /// <summary>
    /// Os pets das reservas que ocupam vaga (aceita, confirmada ou em andamento) no cantinho
    /// <paramref name="listingId"/> e cruzam o período de <paramref name="checkIn"/> a
    /// <paramref name="checkOut"/> (a saída não conta como noite). Um item por pet de cada reserva.
    /// O Listings usa no "Combina com seu pet" (os outros hóspedes do período).
    /// </summary>
    Task<IReadOnlyList<StayingPet>> GetStayingPetsAsync(
        Guid listingId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken);

    /// <summary>
    /// A reserva em andamento (check-in feito, ainda sem check-out) entre o tutor
    /// <paramref name="ownerId"/> e o anfitrião <paramref name="hostId"/>, ou <c>null</c>. Com mais
    /// de uma, a de chegada mais recente. O Conversations liga a ela as mensagens da estadia.
    /// </summary>
    Task<Guid?> GetInProgressBookingIdAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// O tutor <paramref name="ownerId"/> e o anfitrião <paramref name="hostId"/> têm alguma reserva
    /// em aberto entre eles (solicitada, aceita, confirmada ou em andamento)? A conversa dessa dupla
    /// não é apagada pela retenção.
    /// </summary>
    Task<bool> HasOpenBookingBetweenAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken);

    /// <summary>Números de cada anfitrião para os selos. Anfitrião sem nenhuma reserva não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, HostBookingStats>> GetHostStatsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// A reserva como o módulo de pagamento precisa vê-la (status, valor, datas, prazo para pagar e
    /// reembolso), ou <c>null</c> se ela não existe. Sem nome, endereço nem outro dado pessoal.
    /// </summary>
    Task<BookingPaymentView?> GetForPaymentAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>Um pet hospedado no cantinho no período, com o tutor da reserva.</summary>
/// <param name="OwnerId">Tutor da reserva (<c>owner.owners.id</c>).</param>
/// <param name="PetId">Pet da reserva (<c>pet.pets.id</c>).</param>
public sealed record StayingPet(Guid OwnerId, Guid PetId);

/// <summary>Números do anfitrião nas reservas.</summary>
/// <param name="CompletedStays">Estadias concluídas (check-out feito).</param>
/// <param name="AverageResponseTime">
/// Tempo médio entre o pedido e a resposta (aceite ou recusa). Nulo se ele nunca respondeu.
/// </param>
public sealed record HostBookingStats(int CompletedStays, TimeSpan? AverageResponseTime);
