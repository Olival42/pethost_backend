using FluentValidation;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Validation;

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
            .NotEmpty().WithError(ReviewsErrors.PetIdRequired);

        RuleFor(x => x.Rating)
            .NotNull().WithError(ReviewsErrors.PetRatingInvalid)
            .InclusiveBetween(Review.MinRating, Review.MaxRating).WithError(ReviewsErrors.PetRatingInvalid);

        RuleFor(x => x.Comment)
            .MaximumLength(Review.CommentMaxLength).WithError(ReviewsErrors.PetCommentTooLong)
            .Must(comment => comment is null || !Review.HasForbiddenControlCharacters(comment))
            .WithError(ReviewsErrors.PetCommentInvalidCharacters);
    }
}
