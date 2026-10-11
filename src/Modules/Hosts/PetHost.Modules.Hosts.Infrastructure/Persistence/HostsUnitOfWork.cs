using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence;

/// <summary>
/// Confirma a transação do módulo Hosts e traduz as corridas que o banco barra em erro de
/// negócio (§15): concorrência otimista (<c>xmin</c>) e índices únicos.
/// </summary>
/// <remarks>
/// Em falha, o rastreamento é limpo: as mudanças recusadas não voltam a ser tentadas por um
/// próximo <c>SaveChanges</c> no mesmo escopo (a tarefa de limpeza grava várias vezes).
/// </remarks>
internal sealed class HostsUnitOfWork(HostsDbContext dbContext) : IHostsUnitOfWork
{
    private const string UniqueViolation = "23505";

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(HostsErrors.ApplicationConflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation } postgres)
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(ErrorFor(postgres.ConstraintName));
        }
    }

    private static Error ErrorFor(string? constraint) => constraint switch
    {
        "uq_host_applications_user_id_open" => HostsErrors.ApplicationAlreadyOpen,
        "uq_host_applications_cpf_open" or "uq_hosts_cpf_individual" => HostsErrors.CpfAlreadyRegistered,
        "uq_host_applications_cnpj_open" or "uq_hosts_cnpj" => HostsErrors.CnpjAlreadyRegistered,
        "uq_hosts_user_id" => HostsErrors.AlreadyVerified,
        _ => HostsErrors.ApplicationConflict,
    };
}
