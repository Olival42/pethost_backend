using System.Globalization;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Hosts.GetMyDashboard;

/// <summary>Painel do anfitrião logado: pedidos novos, estadias e quanto recebeu no mês.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Month">
/// Mês dos ganhos (<c>yyyy-MM</c>); vazio = o mês atual (Brasília). As reservas são sempre as de agora.
/// </param>
public sealed record GetMyDashboardQuery(Guid UserId, string? Month = null) : IQuery<HostDashboardResponse>;

/// <summary>
/// Junta as reservas (Bookings) e os ganhos (Payments) numa chamada só. Conta sem perfil de
/// anfitrião: <c>404 HOST_NOT_FOUND</c>; mês fora do formato ou no futuro: <c>400</c> em <c>month</c>.
/// </summary>
/// <remarks>
/// As duas leituras são feitas uma depois da outra: a das reservas passa por vários módulos (e pelo
/// <c>DbContext</c> do próprio Hosts, para o nome do anfitrião), e um <c>DbContext</c> não aceita
/// consultas em paralelo.
/// </remarks>
public sealed class GetMyDashboardQueryHandler(
    IHostRepository hostRepository,
    IHostBookings hostBookings,
    IHostEarnings hostEarnings,
    TimeProvider timeProvider) : IQueryHandler<GetMyDashboardQuery, HostDashboardResponse>
{
    /// <summary>Quantos itens cada lista do painel traz.</summary>
    public const int ItemLimit = 5;

    /// <summary>UTC−3: o mês do painel é o de Brasília, como as datas da reserva e os vencimentos.</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public async Task<Result<HostDashboardResponse>> HandleAsync(GetMyDashboardQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(Brasilia).DateTime);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        var month = currentMonth;
        if (!string.IsNullOrWhiteSpace(query.Month))
        {
            if (!DateOnly.TryParseExact(query.Month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month))
                return Result<HostDashboardResponse>.Failure(HostsErrors.DashboardMonthInvalid);

            if (month > currentMonth)
                return Result<HostDashboardResponse>.Failure(HostsErrors.DashboardMonthInFuture);
        }

        var host = await hostRepository.GetByUserIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostDashboardResponse>.Failure(HostsErrors.ProfileNotFound);

        var bookings = await hostBookings.GetOverviewAsync(host.Id.Value, ItemLimit, cancellationToken).ConfigureAwait(false);
        var earnings = await hostEarnings.GetAsync(host.Id.Value, month, cancellationToken).ConfigureAwait(false);
        var completedStays = await hostBookings
            .CountCompletedStaysAsync(host.Id.Value, StartOf(month), StartOf(month.AddMonths(1)), cancellationToken)
            .ConfigureAwait(false);

        return Result<HostDashboardResponse>.Success(
            HostDashboardResponse.From(now, month, bookings, earnings, completedStays, host.StripePayoutsEnabled));
    }

    /// <summary>Meia-noite (Brasília) do 1º dia do mês.</summary>
    private static DateTimeOffset StartOf(DateOnly month) => new(month.ToDateTime(TimeOnly.MinValue), Brasilia);
}
