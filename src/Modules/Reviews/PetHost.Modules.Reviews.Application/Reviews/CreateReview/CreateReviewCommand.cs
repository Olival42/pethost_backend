using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reviews.CreateReview;

/// <summary>
/// Um dos lados avalia uma estadia concluída: o tutor avalia o anfitrião; o anfitrião avalia o
/// tutor e, se quiser, cada pet. Nota de 1 a 5 e comentário opcional.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>. Diz qual lado escreve.</param>
/// <param name="BookingId">A reserva avaliada; o resto sai dela.</param>
/// <param name="Rating">Nota inteira de 1 a 5. Obrigatória.</param>
/// <param name="Comment">Até 1.000 caracteres. Vazio = sem comentário.</param>
/// <param name="Pets">Notas dos pets da reserva. Só o anfitrião manda.</param>
public sealed record CreateReviewCommand(
    Guid UserId,
    string Role,
    Guid BookingId,
    int? Rating,
    string? Comment,
    IReadOnlyList<ReviewPetInput>? Pets = null) : ICommand<ReviewResponse>;
