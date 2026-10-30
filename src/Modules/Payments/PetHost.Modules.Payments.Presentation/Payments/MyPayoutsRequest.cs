namespace PetHost.Modules.Payments.Presentation.Payments;

/// <summary>Paginação dos ganhos do anfitrião (§13).</summary>
/// <param name="Page">Começa em 1.</param>
/// <param name="PageSize">Até 100.</param>
public sealed record MyPayoutsRequest(int Page = 1, int PageSize = 20);
