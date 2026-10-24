using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Modules.Reviews.Domain.Errors;

/// <summary>
/// Todos os erros do módulo Reviews (avaliações). Zero literal inline no resto do código (§6).
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class ReviewsErrors
{
    /// <summary>Maior página das listagens (§13).</summary>
    public const int MaxPageSize = 100;

    /// <summary>Maior lote de cada rodada do lembrete.</summary>
    public const int MaxBatchSize = 1000;

    // --- Validação (VALIDATION_ERROR, 400, acumulável) ---

    public static readonly Error BookingIdRequired =
        Error.Validation("bookingId", "REVIEW_BOOKING_ID_REQUIRED", "Booking id is required.");

    public static readonly Error ReviewIdRequired =
        Error.Validation("reviewId", "REVIEW_REVIEW_ID_REQUIRED", "Review id is required.");

    public static readonly Error RatingRequired =
        Error.Validation("rating", "REVIEW_RATING_REQUIRED", "Rating is required.");

    public static readonly Error RatingInvalid =
        Error.Validation("rating", "REVIEW_RATING_INVALID", $"Rating must be a whole number from {Review.MinRating} to {Review.MaxRating}.", ("min", Review.MinRating), ("max", Review.MaxRating));

    public static readonly Error CommentTooLong =
        Error.Validation("comment", "REVIEW_COMMENT_TOO_LONG", $"Comment must be at most {Review.CommentMaxLength} characters.", ("max", Review.CommentMaxLength));

    public static readonly Error CommentInvalidCharacters =
        Error.Validation("comment", "REVIEW_COMMENT_INVALID_CHARACTERS", "Comment must not contain control characters other than line breaks and tabs.");

    public static readonly Error HostIdInvalid =
        Error.Validation("hostId", "REVIEW_HOST_ID_INVALID", "Host id must not be empty.");

    public static readonly Error ListingIdInvalid =
        Error.Validation("listingId", "REVIEW_LISTING_ID_INVALID", "Listing id must not be empty.");

    public static readonly Error PageInvalid =
        Error.Validation("page", "REVIEW_PAGE_INVALID", "Page must be 1 or greater.");

    public static readonly Error PageSizeInvalid =
        Error.Validation("pageSize", "REVIEW_PAGE_SIZE_INVALID", $"Page size must be between 1 and {MaxPageSize}.", ("max", MaxPageSize));

    public static readonly Error OwnerIdInvalid =
        Error.Validation("ownerId", "REVIEW_OWNER_ID_INVALID", "Owner id must not be empty.");

    public static readonly Error PetIdInvalid =
        Error.Validation("petId", "REVIEW_PET_ID_INVALID", "Pet id must not be empty.");

    public static readonly Error RatingScopeInvalid =
        Error.Validation("hostId", "REVIEW_RATING_SCOPE_INVALID", "Send exactly one of hostId or listingId.");

    // --- Validação: notas dos pets (só na avaliação do anfitrião) ---

    public static readonly Error PetsNotAllowed =
        Error.Validation("pets", "REVIEW_PETS_NOT_ALLOWED", "Only the host rates the pets of the stay.");

    public static readonly Error PetIdRequired =
        Error.Validation("pets", "REVIEW_PET_ID_REQUIRED", "Each pet rating needs the pet id.");

    public static readonly Error PetDuplicated =
        Error.Validation("pets", "REVIEW_PET_DUPLICATED", "Each pet can be rated only once.");

    public static readonly Error PetNotInStay =
        Error.Validation("pets", "REVIEW_PET_NOT_IN_STAY", "Each rated pet must be one of the pets of this stay.");

    public static readonly Error PetRatingInvalid =
        Error.Validation("pets", "REVIEW_PET_RATING_INVALID", $"Each pet rating must be a whole number from {Review.MinRating} to {Review.MaxRating}.", ("min", Review.MinRating), ("max", Review.MaxRating));

    public static readonly Error PetCommentTooLong =
        Error.Validation("pets", "REVIEW_PET_COMMENT_TOO_LONG", $"Each pet comment must be at most {Review.CommentMaxLength} characters.", ("max", Review.CommentMaxLength));

    public static readonly Error PetCommentInvalidCharacters =
        Error.Validation("pets", "REVIEW_PET_COMMENT_INVALID_CHARACTERS", "Pet comments must not contain control characters other than line breaks and tabs.");

    // --- Validação: resposta do avaliado ---

    public static readonly Error ReplyRequired =
        Error.Validation("text", "REVIEW_REPLY_REQUIRED", "Reply text is required.");

    public static readonly Error ReplyTooLong =
        Error.Validation("text", "REVIEW_REPLY_TOO_LONG", $"Reply text must be at most {Review.CommentMaxLength} characters.", ("max", Review.CommentMaxLength));

    public static readonly Error ReplyInvalidCharacters =
        Error.Validation("text", "REVIEW_REPLY_INVALID_CHARACTERS", "Reply text must not contain control characters other than line breaks and tabs.");

    // --- Validação: denúncia e análise do admin ---

    public static readonly Error ReportIdRequired =
        Error.Validation("reportId", "REVIEW_REPORT_ID_REQUIRED", "Report id is required.");

    public static readonly Error ReasonInvalid =
        Error.Validation("reason", "REVIEW_REASON_INVALID", $"Reason must be one of: {string.Join(", ", ReviewReportReasonValues.All)}.");

    public static readonly Error DescriptionRequired =
        Error.Validation("description", "REVIEW_DESCRIPTION_REQUIRED", $"Description is required when the reason is '{ReviewReportReasonValues.Other}'.");

    public static readonly Error DescriptionTooLong =
        Error.Validation("description", "REVIEW_DESCRIPTION_TOO_LONG", $"Description must be at most {ReviewReport.TextMaxLength} characters.", ("max", ReviewReport.TextMaxLength));

    public static readonly Error DescriptionInvalidCharacters =
        Error.Validation("description", "REVIEW_DESCRIPTION_INVALID_CHARACTERS", "Description must not contain control characters other than line breaks and tabs.");

    public static readonly Error OutcomeInvalid =
        Error.Validation("outcome", "REVIEW_OUTCOME_INVALID", $"Outcome must be one of: {string.Join(", ", ReviewReportStatusValues.Outcomes)}.");

    public static readonly Error NoteRequired =
        Error.Validation("note", "REVIEW_NOTE_REQUIRED", "A note is required to resolve a report.");

    public static readonly Error NoteTooLong =
        Error.Validation("note", "REVIEW_NOTE_TOO_LONG", $"Note must be at most {ReviewReport.TextMaxLength} characters.", ("max", ReviewReport.TextMaxLength));

    public static readonly Error NoteInvalidCharacters =
        Error.Validation("note", "REVIEW_NOTE_INVALID_CHARACTERS", "Note must not contain control characters other than line breaks and tabs.");

    public static readonly Error HideReasonRequired =
        Error.Validation("reason", "REVIEW_HIDE_REASON_REQUIRED", "A reason is required to hide a review.");

    public static readonly Error HideReasonTooLong =
        Error.Validation("reason", "REVIEW_HIDE_REASON_TOO_LONG", $"Reason must be at most {Review.CommentMaxLength} characters.", ("max", Review.CommentMaxLength));

    public static readonly Error HideReasonInvalidCharacters =
        Error.Validation("reason", "REVIEW_HIDE_REASON_INVALID_CHARACTERS", "Reason must not contain control characters other than line breaks and tabs.");

    public static readonly Error StatusInvalid =
        Error.Validation("status", "REVIEW_STATUS_INVALID", $"Status must be one of: {string.Join(", ", ReviewReportStatusValues.All)}.");

    // --- Validação: configuração do lembrete (comando da tarefa em segundo plano) ---

    public static readonly Error ReminderDelayInvalid =
        Error.Validation("delayHours", "REVIEW_REMINDER_DELAY_INVALID", $"Reminder delay must be between 1 and {(Review.WindowDays * 24) - 1} hours.", ("max", (Review.WindowDays * 24) - 1));

    public static readonly Error BatchSizeInvalid =
        Error.Validation("batchSize", "REVIEW_BATCH_SIZE_INVALID", $"Batch size must be between 1 and {MaxBatchSize}.", ("max", MaxBatchSize));

    // --- Regras de negócio ---

    /// <summary>
    /// 404: não existe avaliação com esse id, ou ela foi apagada, ou (ao editar e apagar) é de outra
    /// pessoa. As situações respondem igual de propósito (§8).
    /// </summary>
    public static Error NotFound(ReviewId id) =>
        new("REVIEW_NOT_FOUND", $"Review '{id.Value}' was not found.");

    /// <summary>404: a reserva não existe ou não é de quem pede (§8: recurso de outra conta é 404).</summary>
    public static Error BookingNotFound(Guid bookingId) =>
        new("REVIEW_BOOKING_NOT_FOUND", $"Booking '{bookingId}' was not found.");

    /// <summary>404: a conta do token não tem perfil de tutor.</summary>
    public static readonly Error OwnerNotFound =
        new("REVIEW_OWNER_NOT_FOUND", "This account has no owner profile.");

    /// <summary>403: o tutor está inativo ou suspenso e não pode avaliar, editar, responder nem denunciar.</summary>
    public static readonly Error OwnerDeactivated =
        new("REVIEW_OWNER_DEACTIVATED", "This owner profile is deactivated or suspended.");

    /// <summary>404: a conta do token não tem perfil de anfitrião verificado.</summary>
    public static readonly Error HostNotFound =
        new("REVIEW_HOST_NOT_FOUND", "This account has no verified host profile yet.");

    /// <summary>403: o anfitrião está inativo ou suspenso e não pode avaliar, editar, responder nem denunciar.</summary>
    public static readonly Error HostDeactivated =
        new("REVIEW_HOST_DEACTIVATED", "This host profile is deactivated or suspended.");

    /// <summary>422: o admin ocultou a avaliação; não dá para editar nem responder.</summary>
    public static readonly Error HiddenByModeration =
        new("REVIEW_HIDDEN_BY_MODERATION", "This review was hidden by moderation and cannot be changed or replied to.");

    /// <summary>
    /// 422: a avaliação já foi publicada (os dois lados avaliaram ou o prazo acabou) e não muda mais.
    /// Ainda pode ser apagada.
    /// </summary>
    public static readonly Error AlreadyPublished =
        new("REVIEW_ALREADY_PUBLISHED", "This review has already been published and can no longer be changed.");

    /// <summary>422: ninguém denuncia a própria avaliação.</summary>
    public static readonly Error ReportOwnReview =
        new("REVIEW_REPORT_OWN_REVIEW", "You cannot report a review you wrote.");

    /// <summary>409: esta conta já denunciou esta avaliação.</summary>
    public static readonly Error ReportAlreadyExists =
        new("REVIEW_REPORT_ALREADY_EXISTS", "You have already reported this review.");

    /// <summary>404: não existe denúncia com esse id.</summary>
    public static Error ReportNotFound(ReviewReportId id) =>
        new("REVIEW_REPORT_NOT_FOUND", $"Report '{id.Value}' was not found.");

    /// <summary>422: a denúncia já foi analisada.</summary>
    public static readonly Error ReportAlreadyResolved =
        new("REVIEW_REPORT_ALREADY_RESOLVED", "This report has already been resolved.");

    /// <summary>422: só a estadia concluída (check-out feito) pode ser avaliada.</summary>
    public static readonly Error BookingNotCompleted =
        new("REVIEW_BOOKING_NOT_COMPLETED", "Only a completed stay (after check-out) can be reviewed.");

    /// <summary>422: passou o prazo para avaliar ou editar (7 dias depois do check-out).</summary>
    public static readonly Error WindowClosed =
        new Error("REVIEW_WINDOW_CLOSED", $"Reviews can only be written or changed up to {Review.WindowDays} days after check-out.").WithParams(("days", Review.WindowDays));

    /// <summary>409: este lado já avaliou a reserva (um por lado; a apagada não libera outra).</summary>
    public static readonly Error AlreadyExists =
        new("REVIEW_ALREADY_EXISTS", "You have already reviewed this booking.");

    /// <summary>409: a avaliação mudou ao mesmo tempo (outro request do autor).</summary>
    public static readonly Error Conflict =
        new("REVIEW_CONFLICT", "This review was changed at the same time. Reload it and try again.");
}
