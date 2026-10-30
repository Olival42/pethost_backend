using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.TestKit;

/// <summary>
/// Monta uma avaliação para teste: por padrão, a Camila (tutora) avalia com nota 5 a estadia da
/// Pipoca na casa da Dona Cida, um dia depois do check-out. <see cref="ByHost"/> troca o lado: a
/// Dona Cida avalia a Camila (e a Pipoca). Cada <c>With...</c> troca uma parte; o teste declara só o
/// que importa. Os ids são fixos, para as asserções compararem.
/// </summary>
public sealed class ReviewBuilder
{
    /// <summary>Check-out da estadia: sábado, 10/10/2026, 12:00 UTC.</summary>
    public static readonly DateTimeOffset CheckedOutAt = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Um dia depois do check-out, dentro do prazo de 7 dias.</summary>
    public static readonly DateTimeOffset DefaultNow = CheckedOutAt.AddDays(1);

    /// <summary>Perfil de tutora da Camila (<c>owner.owners.id</c>).</summary>
    public static readonly Guid OwnerId = Guid.Parse("0199d000-0000-7000-8000-000000000001");

    /// <summary>Conta da Camila (claim <c>sub</c>).</summary>
    public static readonly Guid OwnerUserId = Guid.Parse("0199d000-0000-7000-8000-000000000011");

    /// <summary>Perfil de anfitriã da Dona Cida (<c>host.hosts.id</c>).</summary>
    public static readonly Guid HostId = Guid.Parse("0199d000-0000-7000-8000-000000000002");

    /// <summary>Conta da Dona Cida (claim <c>sub</c>).</summary>
    public static readonly Guid HostUserId = Guid.Parse("0199d000-0000-7000-8000-000000000012");

    /// <summary>O cantinho da Dona Cida.</summary>
    public static readonly Guid ListingId = Guid.Parse("0199d000-0000-7000-8000-000000000021");

    /// <summary>A reserva concluída.</summary>
    public static readonly Guid BookingId = Guid.Parse("0199d000-0000-7000-8000-000000000031");

    /// <summary>A Pipoca, o pet da reserva.</summary>
    public static readonly Guid PetId = Guid.Parse("0199d000-0000-7000-8000-000000000041");

    private ReviewParty _side = ReviewParty.Owner;
    private Guid _ownerId = OwnerId;
    private int _rating = 5;
    private string? _comment = "A Pipoca voltou feliz!";
    private IReadOnlyList<PetRatingInput>? _pets;
    private DateTimeOffset _now = DefaultNow;

    /// <summary>A estadia concluída padrão (da Camila, com a Pipoca, na casa da Dona Cida).</summary>
    public static ReviewedStay CompletedStay(DateTimeOffset? checkedOutAt = null) =>
        new(BookingId, OwnerId, HostId, ListingId, IsCompleted: true, checkedOutAt ?? CheckedOutAt, [PetId]);

    /// <summary>A Dona Cida avalia a Camila, com nota 4 para a Pipoca.</summary>
    public ReviewBuilder ByHost()
    {
        _side = ReviewParty.Host;
        _comment = "Tutora pontual e atenciosa.";
        _pets = [new PetRatingInput(PetId, 4, "Latiu um pouco à noite.")];
        return this;
    }

    public ReviewBuilder WithOwner(Guid ownerId)
    {
        _ownerId = ownerId;
        return this;
    }

    public ReviewBuilder WithRating(int rating)
    {
        _rating = rating;
        return this;
    }

    public ReviewBuilder WithComment(string? comment)
    {
        _comment = comment;
        return this;
    }

    public ReviewBuilder WrittenAt(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    public Review Build()
    {
        var stay = CompletedStay() with { OwnerId = _ownerId };
        var author = _side == ReviewParty.Owner ? _ownerId : HostId;
        var review = Review.Create(stay, _side, author, _rating, _comment, _pets, _now);

        return review.IsSuccess
            ? review.Value!
            : throw new InvalidOperationException($"Invalid test review: {review.FirstError?.Code}");
    }
}
