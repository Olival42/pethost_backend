namespace PetHost.Shared.Contracts.Storage;

/// <summary>External effects that must follow the outcome of a database transaction.</summary>
public interface ICommitEffects
{
    bool IsActive { get; }

    void AfterCommit(Func<CancellationToken, Task> action);

    void AfterRollback(Func<CancellationToken, Task> action);
}
