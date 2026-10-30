using Microsoft.Extensions.Logging;
using PetHost.Shared.Contracts.Storage;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>Request-scoped bucket compensation and notifications for direct upload confirmation.</summary>
public sealed partial class UploadEffects(ILogger<UploadEffects> logger) : ICommitEffects
{
    private readonly List<Func<CancellationToken, Task>> _committed = [];
    private readonly List<Func<CancellationToken, Task>> _rolledBack = [];

    public bool IsActive { get; private set; }

    public void Begin()
    {
        if (IsActive)
            throw new InvalidOperationException("An upload confirmation is already active in this scope.");
        IsActive = true;
    }

    public void AfterCommit(Func<CancellationToken, Task> action) => _committed.Add(action);

    public void AfterRollback(Func<CancellationToken, Task> action) => _rolledBack.Add(action);

    public Task CommitAsync() => FinishAsync(_committed);

    public Task RollbackAsync() => FinishAsync(_rolledBack);

    internal (int Commit, int Rollback) Checkpoint() => (_committed.Count, _rolledBack.Count);

    internal async Task RollbackResourceAsync((int Commit, int Rollback) checkpoint)
    {
        var compensation = _rolledBack.Skip(checkpoint.Rollback).ToArray();
        _committed.RemoveRange(checkpoint.Commit, _committed.Count - checkpoint.Commit);
        _rolledBack.RemoveRange(checkpoint.Rollback, _rolledBack.Count - checkpoint.Rollback);
        // The resource failed, but its rejection receipt can still commit. Compensate only this resource.
        IsActive = false;
        try
        {
            foreach (var action in compensation)
            {
#pragma warning disable CA1031 // Compensation failure must not hide the original rejection.
                try { await action(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) { LogEffectFailed(logger, exception); }
#pragma warning restore CA1031
            }
        }
        finally { IsActive = true; }
    }

    /// <summary>Unknown commit outcome: preserve both files until a later request can read the receipt.</summary>
    public void Discard()
    {
        IsActive = false;
        _committed.Clear();
        _rolledBack.Clear();
    }

    private async Task FinishAsync(List<Func<CancellationToken, Task>> actions)
    {
        IsActive = false;
        try
        {
            foreach (var action in actions)
            {
                try
                {
                    await action(CancellationToken.None).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A committed database operation must not be reported as failed by an external effect.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    LogEffectFailed(logger, exception);
                }
            }
        }
        finally
        {
            _committed.Clear();
            _rolledBack.Clear();
        }
    }

    [LoggerMessage(EventId = 6043, Level = LogLevel.Warning, Message = "An upload transaction external effect failed.")]
    private static partial void LogEffectFailed(ILogger logger, Exception exception);
}
