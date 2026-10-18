using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews;

/// <summary>A nota de um pet, como vem no corpo (só o anfitrião manda).</summary>
/// <param name="PetId">Um dos pets da reserva.</param>
/// <param name="Rating">Nota inteira de 1 a 5.</param>
/// <param name="Comment">Até 1.000 caracteres. Opcional.</param>
public sealed record ReviewPetInput(Guid PetId, int? Rating, string? Comment)
{
    /// <summary>Para o domínio. Só chame depois do validador (nota presente).</summary>
    public static IReadOnlyList<PetRatingInput>? ToDomain(IReadOnlyList<ReviewPetInput>? pets) =>
        pets is null ? null : [.. pets.Select(p => new PetRatingInput(p.PetId, p.Rating.GetValueOrDefault(), p.Comment))];
}

/// <summary>Formato de cada nota de pet. Se o pet é da reserva é conferido pelo agregado.</summary>
public sealed class ReviewPetInputValidator : AbstractValidator<ReviewPetInput>
{
    public ReviewPetInputValidator()
    {
        RuleFor(x => x.PetId)
            .NotEmpty().WithMessage(ReviewsErrors.PetIdRequired.Message);

        RuleFor(x => x.Rating)
            .NotNull().WithMessage(ReviewsErrors.PetRatingInvalid.Message)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithMessage(ReviewsErrors.PetRatingInvalid.Message);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithMessage(ReviewsErrors.PetCommentTooLong.Message)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithMessage(ReviewsErrors.PetCommentInvalidCharacters.Message);
    }
}
