using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// Confirma a transação do módulo Conversations e traduz as corridas que o banco barra em erro de
/// negócio (§15): o índice único da dupla, a denúncia repetida da mesma mensagem e a conversa
/// apagada pela retenção no meio do envio ou da denúncia.
/// </summary>
/// <remarks>
/// Em falha, o rastreamento é limpo: as mudanças recusadas não voltam a ser tentadas por um
/// próximo <c>SaveChanges</c> no mesmo escopo.
/// </remarks>
internal sealed class ConversationsUnitOfWork(ConversationsDbContext dbContext) : IConversationsUnitOfWork
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            // A conversa sumiu entre a leitura e a gravação (a retenção apagou), ou outro admin
            // analisou a mesma denúncia antes (xmin).
            dbContext.ChangeTracker.Clear();
            return Result.Failure(ConversationsErrors.Conflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation or ForeignKeyViolation } postgres)
        {
            dbContext.ChangeTracker.Clear();
            return Result.Failure(ErrorFor(postgres.ConstraintName));
        }
    }

    /// <summary>
    /// Trava da conversa: <c>pg_advisory_xact_lock</c> com uma chave de 64 bits tirada do id, como a
    /// trava do cantinho no Bookings. Vale até o fim da transação; quem chega depois espera e já lê o
    /// que o primeiro gravou.
    /// </summary>
    public async Task<T> RunLockedOnConversationAsync<T>(
        ConversationId conversationId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Upload confirmation already owns an ambient transaction across the module and upload receipt.
        await using var transaction = System.Transactions.Transaction.Current is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var key = $"conversation:{conversationId.Value}";
        await dbContext.Database
            .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken)
            .ConfigureAwait(false);

        var result = await work(cancellationToken).ConfigureAwait(false);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    private static Error ErrorFor(string? constraint) => constraint switch
    {
        "uq_conversations_owner_id_host_id" => ConversationsErrors.AlreadyExists,
        "uq_message_reports_message_id" => ConversationsErrors.ReportAlreadyExists,
        _ => ConversationsErrors.Conflict,
    };
}
