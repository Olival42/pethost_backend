namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>
/// A nota que o anfitrião dá a um pet da estadia (o comportamento dele), dentro da avaliação do
/// tutor. Espelha a tabela <c>review.review_pet_ratings</c>.
/// </summary>
public sealed class ReviewPetRating
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private ReviewPetRating()
    {
    }

    internal ReviewPetRating(Guid petId, int rating, string? comment)
    {
        PetId = petId;
        Rating = rating;
        Comment = comment;
    }

    /// <summary>O pet da reserva (<c>pet.pets.id</c>, FK lógica).</summary>
    public Guid PetId { get; private set; }

    /// <summary>Nota inteira de 1 a 5.</summary>
    public int Rating { get; private set; }

    /// <summary>Como foi com o pet. Nulo quando o anfitrião só deu a nota.</summary>
    public string? Comment { get; private set; }
}

/// <summary>O que o anfitrião manda para cada pet: o pet, a nota e o comentário opcional.</summary>
public sealed record PetRatingInput(Guid PetId, int Rating, string? Comment);
