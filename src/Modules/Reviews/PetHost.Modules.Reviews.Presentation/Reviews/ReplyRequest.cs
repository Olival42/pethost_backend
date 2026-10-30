namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Corpo de <c>PUT /api/v1/reviews/{reviewId}/reply</c>.</summary>
/// <param name="Text">A resposta pública, até 1.000 caracteres.</param>
public sealed record ReplyRequest(string? Text);
