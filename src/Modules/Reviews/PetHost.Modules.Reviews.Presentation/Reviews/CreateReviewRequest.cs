using PetHost.Modules.Reviews.Application.Reviews;

namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Corpo de <c>POST /api/v1/reviews</c>.</summary>
public sealed class CreateReviewRequest
{
    /// <summary>A reserva concluída que se avalia.</summary>
    public Guid BookingId { get; init; }

    /// <summary>Nota inteira de 1 a 5.</summary>
    public int? Rating { get; init; }

    /// <summary>Comentário opcional, até 1.000 caracteres.</summary>
    public string? Comment { get; init; }

    /// <summary>Notas dos pets da reserva (<c>petId</c>, <c>rating</c>, <c>comment</c>). Só o anfitrião manda.</summary>
    public IReadOnlyList<ReviewPetInput>? Pets { get; init; }
}
