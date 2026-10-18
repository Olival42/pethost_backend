namespace PetHost.Modules.Reviews.Presentation.Reports;

/// <summary>Corpo de <c>PUT /api/v1/reviews/{reviewId}/moderation-hide</c>.</summary>
/// <param name="Reason">Por que ocultar. O autor vê.</param>
public sealed record HideReviewRequest(string? Reason);
