using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Domain.Reviews;

/// <summary>
/// A avaliação de uma estadia concluída, escrita por um dos lados (<see cref="AuthorSide"/>): o
/// tutor avalia o anfitrião e o cantinho; o anfitrião avalia o tutor e, opcionalmente, cada pet
/// (<see cref="PetRatings"/>). Nota inteira de 1 a 5 e comentário opcional. Uma por lado por
/// reserva. Espelha a tabela <c>review.reviews</c>.
/// </summary>
/// <remarks>
/// Guarda sempre o tutor, o anfitrião <b>e</b> o cantinho da reserva (FKs lógicas), copiados do
/// Bookings na criação. Quem lê agrega como fizer sentido: pessoa física tem um cantinho só, então
/// "do anfitrião" e "do cantinho" dão o mesmo; empresa tem um cantinho por filial, e a nota da
/// filial é a do cantinho. Também guarda quando a estadia acabou (<see cref="StayEndedAt"/>): o
/// prazo de edição é conferido sem perguntar ao Bookings de novo. O avaliado pode responder uma vez
/// (<see cref="ReplyText"/>), e o admin pode ocultar a avaliação (<see cref="HiddenAt"/>).
/// <para>
/// <b>Às cegas:</b> ninguém além do autor lê a avaliação antes de <see cref="PublishedAt"/>. Ela nasce
/// marcada para o fim do prazo (7 dias depois do check-out); quando o outro lado também avalia, as
/// duas são publicadas na hora (<see cref="Publish"/>). Assim nenhum lado escreve sabendo o que o
/// outro disse. Publicada, não muda mais (só pode ser apagada).
/// </para>
/// </remarks>
public sealed class Review : Entity<ReviewId>
{
    /// <summary>Menor nota.</summary>
    public const int MinRating = 1;

    /// <summary>Maior nota.</summary>
    public const int MaxRating = 5;

    /// <summary>Maior comentário (da avaliação, de cada pet e da resposta), em caracteres.</summary>
    public const int CommentMaxLength = 1000;

    /// <summary>Quantos dias depois do check-out se pode avaliar e editar (escopo, regra 9).</summary>
    public const int WindowDays = 7;

    private readonly List<ReviewPetRating> _petRatings = [];

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Review()
    {
    }

    private Review(
        ReviewedStay stay,
        ReviewParty authorSide,
        DateTimeOffset stayEndedAt,
        int rating,
        string? comment,
        IEnumerable<ReviewPetRating> petRatings,
        DateTimeOffset now)
        : base(ReviewId.New())
    {
        BookingId = stay.BookingId;
        AuthorSide = authorSide;
        OwnerId = stay.OwnerId;
        HostId = stay.HostId;
        ListingId = stay.ListingId;
        StayEndedAt = stayEndedAt;
        Rating = rating;
        Comment = comment;
        CreatedAt = now;
        UpdatedAt = now;
        PublishedAt = stayEndedAt.AddDays(WindowDays);
        _petRatings.AddRange(petRatings);
    }

    /// <summary>A reserva avaliada (<c>booking.bookings.id</c>). Única por lado.</summary>
    public Guid BookingId { get; private set; }

    /// <summary>Quem escreveu: o tutor (<see cref="ReviewParty.Owner"/>) ou o anfitrião.</summary>
    public ReviewParty AuthorSide { get; private set; }

    /// <summary>Quem é avaliado: o outro lado.</summary>
    public ReviewParty SubjectSide => ReviewPartyValues.Other(AuthorSide);

    /// <summary>O tutor da reserva (<c>owner.owners.id</c>): o autor ou o avaliado.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>O anfitrião da reserva (<c>host.hosts.id</c>): o avaliado ou o autor.</summary>
    public Guid HostId { get; private set; }

    /// <summary>O cantinho onde o pet ficou (<c>listing.listings.id</c>).</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Quando a estadia acabou (check-out). Base do prazo de 7 dias.</summary>
    public DateTimeOffset StayEndedAt { get; private set; }

    /// <summary>Nota inteira de <see cref="MinRating"/> a <see cref="MaxRating"/>.</summary>
    public int Rating { get; private set; }

    /// <summary>Comentário. Nulo quando o autor só deu a nota.</summary>
    public string? Comment { get; private set; }

    /// <summary>Notas dos pets (só na avaliação do anfitrião).</summary>
    public IReadOnlyList<ReviewPetRating> PetRatings => _petRatings.AsReadOnly();

    /// <summary>A resposta pública do avaliado. Nula enquanto ele não respondeu.</summary>
    public string? ReplyText { get; private set; }

    /// <summary>Quando o avaliado respondeu (ou editou a resposta).</summary>
    public DateTimeOffset? RepliedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Quando o autor apagou. Apagada some das listas, mas a reserva não pode ser avaliada de novo por esse lado.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// A partir de quando os outros leem a avaliação: o fim do prazo, ou antes, quando o outro lado
    /// também avalia (<see cref="Publish"/>).
    /// </summary>
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>Quando o avaliado recebeu o e-mail de "você recebeu uma avaliação". Nulo até lá.</summary>
    public DateTimeOffset? SubjectNotifiedAt { get; private set; }

    /// <summary>Os outros já podem ler a avaliação em <paramref name="now"/>?</summary>
    public bool IsPublished(DateTimeOffset now) => PublishedAt <= now;

    /// <summary>Quando o admin ocultou (denúncia procedente). Oculta some das listas e da média.</summary>
    public DateTimeOffset? HiddenAt { get; private set; }

    /// <summary>Conta do admin que ocultou (<c>auth.users.id</c>).</summary>
    public Guid? HiddenBy { get; private set; }

    /// <summary>Por que o admin ocultou. O autor vê; vai para a trilha.</summary>
    public string? HiddenReason { get; private set; }

    /// <summary>O autor apagou a avaliação.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>O admin ocultou a avaliação.</summary>
    public bool IsHidden => HiddenAt is not null;

    /// <summary>Até quando dá para editar: <see cref="WindowDays"/> dias depois do check-out.</summary>
    public DateTimeOffset EditableUntil => StayEndedAt.AddDays(WindowDays);

    /// <summary>
    /// O lado <paramref name="authorSide"/> (perfil <paramref name="authorProfileId"/>) avalia a
    /// estadia. Reserva de outra pessoa: <c>REVIEW_BOOKING_NOT_FOUND</c> (404, como se não existisse).
    /// Antes do check-out: <c>REVIEW_BOOKING_NOT_COMPLETED</c>. Mais de <see cref="WindowDays"/> dias
    /// depois: <c>REVIEW_WINDOW_CLOSED</c>. Só o anfitrião dá nota aos pets, e só aos pets da
    /// reserva. O comentário é aparado; vazio vira nulo.
    /// </summary>
    public static Result<Review> Create(
        ReviewedStay stay,
        ReviewParty authorSide,
        Guid authorProfileId,
        int rating,
        string? comment,
        IReadOnlyList<PetRatingInput>? pets,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stay);

        if (!stay.HasParticipant(authorSide, authorProfileId))
            return Result<Review>.Failure(ReviewsErrors.BookingNotFound(stay.BookingId));

        if (!stay.IsCompleted || stay.CheckedOutAt is not { } checkedOutAt)
            return Result<Review>.Failure(ReviewsErrors.BookingNotCompleted);

        if (now > checkedOutAt.AddDays(WindowDays))
            return Result<Review>.Failure(ReviewsErrors.WindowClosed);

        var content = ValidateContent(rating, comment);
        if (content is not null)
            return Result<Review>.Failure(content);

        var petRatings = BuildPetRatings(authorSide, pets, stay.PetIds);
        if (petRatings.IsFailure)
            return Result<Review>.FromFailure(petRatings);

        return Result<Review>.Success(
            new Review(stay, authorSide, checkedOutAt, rating, NormalizeText(comment), petRatings.Value!, now));
    }

    /// <summary>Quem escreveu é o perfil <paramref name="profileId"/> do lado <paramref name="party"/>?</summary>
    public bool IsWrittenBy(ReviewParty party, Guid profileId) => party == AuthorSide && IsProfile(party, profileId);

    /// <summary>Quem é avaliado é o perfil <paramref name="profileId"/> do lado <paramref name="party"/>?</summary>
    public bool IsAbout(ReviewParty party, Guid profileId) => party == SubjectSide && IsProfile(party, profileId);

    /// <summary>
    /// O autor muda a nota, o comentário e/ou as notas dos pets (PATCH): <c>null</c> mantém;
    /// comentário vazio (<c>""</c>) apaga o comentário; <paramref name="pets"/> substitui a lista
    /// inteira e precisa dos pets da reserva (<paramref name="stayPetIds"/>). Só dentro do prazo
    /// (<c>REVIEW_WINDOW_CLOSED</c>), antes da publicação (<c>REVIEW_ALREADY_PUBLISHED</c>: depois de
    /// ler a do outro lado, ninguém muda a sua) e se o admin não ocultou (<c>REVIEW_HIDDEN_BY_MODERATION</c>).
    /// Devolve os campos que mudaram (vazio = nada mudou, nada a gravar).
    /// </summary>
    public Result<IReadOnlyList<string>> Update(
        int? rating,
        string? comment,
        IReadOnlyList<PetRatingInput>? pets,
        IReadOnlyCollection<Guid>? stayPetIds,
        DateTimeOffset now)
    {
        if (IsHidden)
            return Result<IReadOnlyList<string>>.Failure(ReviewsErrors.HiddenByModeration);

        if (now > EditableUntil)
            return Result<IReadOnlyList<string>>.Failure(ReviewsErrors.WindowClosed);

        if (IsPublished(now))
            return Result<IReadOnlyList<string>>.Failure(ReviewsErrors.AlreadyPublished);

        var newRating = rating ?? Rating;
        var newComment = comment is null ? Comment : NormalizeText(comment);

        var content = ValidateContent(newRating, newComment);
        if (content is not null)
            return Result<IReadOnlyList<string>>.Failure(content);

        List<ReviewPetRating>? newPets = null;
        if (pets is not null)
        {
            var built = BuildPetRatings(AuthorSide, pets, stayPetIds ?? []);
            if (built.IsFailure)
                return Result<IReadOnlyList<string>>.FromFailure(built);

            newPets = built.Value!;
        }

        var changed = new List<string>();

        if (newRating != Rating)
        {
            Rating = newRating;
            changed.Add("rating");
        }

        if (!string.Equals(newComment, Comment, StringComparison.Ordinal))
        {
            Comment = newComment;
            changed.Add("comment");
        }

        if (newPets is not null && !SamePets(newPets))
        {
            _petRatings.Clear();
            _petRatings.AddRange(newPets);
            changed.Add("pets");
        }

        if (changed.Count > 0)
            UpdatedAt = now;

        return Result<IReadOnlyList<string>>.Success(changed);
    }

    /// <summary>
    /// O avaliado (perfil <paramref name="profileId"/> do lado <paramref name="party"/>) responde
    /// em público, ou troca a resposta. Uma resposta por avaliação, sem prazo. Quem não é o avaliado:
    /// <c>REVIEW_NOT_FOUND</c>. Oculta pelo admin: <c>REVIEW_HIDDEN_BY_MODERATION</c>. Devolve se mudou.
    /// </summary>
    public Result<bool> Reply(ReviewParty party, Guid profileId, string? text, DateTimeOffset now)
    {
        // Antes da publicação o avaliado nem lê a avaliação: para ele, ela não existe.
        if (!IsAbout(party, profileId) || !IsPublished(now))
            return Result<bool>.Failure(ReviewsErrors.NotFound(Id));

        if (IsHidden)
            return Result<bool>.Failure(ReviewsErrors.HiddenByModeration);

        var reply = NormalizeText(text);
        if (reply is null)
            return Result<bool>.Failure(ReviewsErrors.ReplyRequired);

        if (reply.Length > CommentMaxLength)
            return Result<bool>.Failure(ReviewsErrors.ReplyTooLong);

        if (HasForbiddenControlCharacters(reply))
            return Result<bool>.Failure(ReviewsErrors.ReplyInvalidCharacters);

        if (string.Equals(reply, ReplyText, StringComparison.Ordinal))
            return Result<bool>.Success(false);

        ReplyText = reply;
        RepliedAt = now;
        UpdatedAt = now;

        return Result<bool>.Success(true);
    }

    /// <summary>O avaliado apaga a resposta. Sem resposta: nada muda (idempotente). Devolve se mudou.</summary>
    public Result<bool> RemoveReply(ReviewParty party, Guid profileId, DateTimeOffset now)
    {
        if (!IsAbout(party, profileId))
            return Result<bool>.Failure(ReviewsErrors.NotFound(Id));

        if (ReplyText is null)
            return Result<bool>.Success(false);

        ReplyText = null;
        RepliedAt = null;
        UpdatedAt = now;

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Publica a avaliação agora (o outro lado também avaliou). Já publicada: nada muda. Devolve se mudou.
    /// </summary>
    public bool Publish(DateTimeOffset now)
    {
        if (IsPublished(now))
            return false;

        PublishedAt = now;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Anota que o avaliado recebeu o aviso por e-mail (ou que não há a quem avisar).</summary>
    public void MarkSubjectNotified(DateTimeOffset now) => SubjectNotifiedAt ??= now;

    /// <summary>
    /// O autor apaga a avaliação (soft delete): ela some das listas e da média. A qualquer momento,
    /// mesmo depois do prazo ou oculta. A reserva não pode ser avaliada de novo por esse lado.
    /// </summary>
    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return;

        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// O admin <paramref name="adminUserId"/> oculta a avaliação (denúncia procedente), com motivo.
    /// Devolve se mudou (já oculta é idempotente).
    /// </summary>
    public bool Hide(Guid adminUserId, string reason, DateTimeOffset now)
    {
        if (adminUserId == Guid.Empty)
            throw new ArgumentException("The admin id cannot be empty.", nameof(adminUserId));

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsHidden)
            return false;

        HiddenAt = now;
        HiddenBy = adminUserId;
        HiddenReason = reason;
        UpdatedAt = now;

        return true;
    }

    /// <summary>O admin volta a mostrar a avaliação (ocultada por engano). Devolve se mudou.</summary>
    public bool Unhide(DateTimeOffset now)
    {
        if (!IsHidden)
            return false;

        HiddenAt = null;
        HiddenBy = null;
        HiddenReason = null;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Apara as pontas; vazio (ou só espaço) vira nulo.</summary>
    public static string? NormalizeText(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Caractere de controle que não seja quebra de linha ou tabulação.</summary>
    public static bool HasForbiddenControlCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');
    }

    private bool IsProfile(ReviewParty party, Guid profileId) =>
        party == ReviewParty.Owner ? OwnerId == profileId : HostId == profileId;

    /// <summary>Nota fora de 1 a 5, comentário longo demais ou com caractere de controle.</summary>
    private static Error? ValidateContent(int rating, string? comment)
    {
        if (rating is < MinRating or > MaxRating)
            return ReviewsErrors.RatingInvalid;

        if (comment is null)
            return null;

        if (comment.Length > CommentMaxLength)
            return ReviewsErrors.CommentTooLong;

        return HasForbiddenControlCharacters(comment) ? ReviewsErrors.CommentInvalidCharacters : null;
    }

    /// <summary>
    /// As notas dos pets: só o anfitrião manda; cada pet uma vez e da reserva; nota de 1 a 5;
    /// comentário como o da avaliação.
    /// </summary>
    private static Result<List<ReviewPetRating>> BuildPetRatings(
        ReviewParty authorSide,
        IReadOnlyList<PetRatingInput>? pets,
        IReadOnlyCollection<Guid> stayPetIds)
    {
        if (pets is null || pets.Count == 0)
            return Result<List<ReviewPetRating>>.Success([]);

        if (authorSide != ReviewParty.Host)
            return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetsNotAllowed);

        if (pets.Select(p => p.PetId).Distinct().Count() != pets.Count)
            return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetDuplicated);

        var ratings = new List<ReviewPetRating>(pets.Count);

        foreach (var pet in pets)
        {
            if (!stayPetIds.Contains(pet.PetId))
                return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetNotInStay);

            if (pet.Rating is < MinRating or > MaxRating)
                return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetRatingInvalid);

            var comment = NormalizeText(pet.Comment);
            if (comment is { Length: > CommentMaxLength })
                return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetCommentTooLong);

            if (comment is not null && HasForbiddenControlCharacters(comment))
                return Result<List<ReviewPetRating>>.Failure(ReviewsErrors.PetCommentInvalidCharacters);

            ratings.Add(new ReviewPetRating(pet.PetId, pet.Rating, comment));
        }

        return Result<List<ReviewPetRating>>.Success(ratings);
    }

    private bool SamePets(List<ReviewPetRating> other) =>
        other.Count == _petRatings.Count
        && other.All(o => _petRatings.Any(p => p.PetId == o.PetId && p.Rating == o.Rating && string.Equals(p.Comment, o.Comment, StringComparison.Ordinal)));
}
