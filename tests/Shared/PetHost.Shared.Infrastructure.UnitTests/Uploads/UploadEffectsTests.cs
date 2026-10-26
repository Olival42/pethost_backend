using Microsoft.Extensions.Logging.Abstractions;
using PetHost.Shared.Infrastructure.Uploads;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Uploads;

public sealed class UploadEffectsTests
{
    [Fact]
    public async Task Commit_Should_RunOnlyCommitEffects_When_TheDatabaseSucceeded()
    {
        var effects = new UploadEffects(NullLogger<UploadEffects>.Instance);
        var committed = false;
        var rolledBack = false;
        effects.Begin();
        effects.AfterCommit(_ => { committed = true; Assert.False(effects.IsActive); return Task.CompletedTask; });
        effects.AfterRollback(_ => { rolledBack = true; return Task.CompletedTask; });
        Assert.False(committed);
        await effects.CommitAsync();
        Assert.True(committed);
        Assert.False(rolledBack);
    }

    [Fact]
    public async Task Rollback_Should_CompensateNewFilesWithoutDeletingOldFiles_When_TheDatabaseFailed()
    {
        var effects = new UploadEffects(NullLogger<UploadEffects>.Instance);
        var oldDeleted = false;
        var newDeleted = false;
        effects.Begin();
        effects.AfterCommit(_ => { oldDeleted = true; return Task.CompletedTask; });
        effects.AfterRollback(_ => { newDeleted = true; return Task.CompletedTask; });
        await effects.RollbackAsync();
        Assert.False(oldDeleted);
        Assert.True(newDeleted);
    }

    [Fact]
    public async Task Commit_Should_ContinueRemainingEffectsWithoutThrowing_When_OneNotificationFails()
    {
        var effects = new UploadEffects(NullLogger<UploadEffects>.Instance);
        var ran = false;
        effects.Begin();
        effects.AfterCommit(_ => throw new InvalidOperationException("simulated delivery failure"));
        effects.AfterCommit(_ => { ran = true; return Task.CompletedTask; });
        await effects.CommitAsync();
        Assert.True(ran);
    }

    [Fact]
    public async Task Discard_Should_PreserveBothFiles_When_TheCommitOutcomeIsUnknown()
    {
        var effects = new UploadEffects(NullLogger<UploadEffects>.Instance);
        var deleted = false;
        effects.Begin();
        effects.AfterCommit(_ => { deleted = true; return Task.CompletedTask; });
        effects.AfterRollback(_ => { deleted = true; return Task.CompletedTask; });
        effects.Discard();
        await effects.RollbackAsync();
        Assert.False(deleted);
        Assert.False(effects.IsActive);
    }
}
