using PetHost.Modules.Reviews.Application.Reviews;

namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Corpo de <c>PATCH /api/v1/reviews/{reviewId}</c>: só muda o que vier.</summary>
public sealed class UpdateReviewRequest
{
    /// <summary>Nota nova, de 1 a 5. Ausente ou <c>null</c>: mantém.</summary>
    public int? Rating { get; init; }

    /// <summary>Comentário novo. Ausente ou <c>null</c>: mantém; <c>""</c>: apaga o comentário.</summary>
    public string? Comment { get; init; }

    /// <summary>Notas dos pets (só o anfitrião). Ausente ou <c>null</c>: mantém; lista: substitui todas.</summary>
    public IReadOnlyList<ReviewPetInput>? Pets { get; init; }
}
