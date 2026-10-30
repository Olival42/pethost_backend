using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Infrastructure.Persistence;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.IntegrationTests.Persistence;

/// <summary>
/// A trava da conversa no banco (<c>pg_advisory_xact_lock</c>) e a releitura dentro dela: é o que
/// impede dois envios ao mesmo tempo de voltarem a última atividade no tempo. Cada escopo de DI
/// faz o papel de uma requisição.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationsUnitOfWorkTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private readonly Conversation _conversation = new ConversationBuilder().Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationsDbContext>();
        conversations.Conversations.Add(_conversation);
        await conversations.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ReloadInsideTheLock_Should_KeepTheNewerLastActivity_When_AnotherRequestSavedFirst()
    {
        // Arrange
        var older = ConversationBuilder.DefaultNow.AddMinutes(1);
        var newer = ConversationBuilder.DefaultNow.AddMinutes(2);
        await using var slow = fixture.Services.CreateAsyncScope();
        var slowRepository = slow.ServiceProvider.GetRequiredService<IConversationRepository>();
        var slowUnitOfWork = slow.ServiceProvider.GetRequiredService<IConversationsUnitOfWork>();
        var stale = (await slowRepository.GetByIdAsync(_conversation.Id, Ct))!;
        await RecordInAnotherRequestAsync(newer);

        // Act
        var saved = await slowUnitOfWork.RunLockedOnConversationAsync(
            _conversation.Id,
            async ct =>
            {
                await slowRepository.ReloadAsync(stale, ct);
                stale.RecordMessage(older);
                return await slowUnitOfWork.SaveChangesAsync(ct);
            },
            Ct);

        // Assert
        saved.IsSuccess.Should().BeTrue();
        (await LastMessageAtAsync()).Should().Be(newer, "a mensagem mais antiga, gravada por último, não pode voltar o horário");
    }

    [Fact]
    public async Task RunLockedOnConversationAsync_Should_MakeTheSecondRequestWait_When_TheFirstHoldsTheLock()
    {
        // Arrange
        var release = new TaskCompletionSource();
        var firstInside = new TaskCompletionSource();
        var secondEntered = false;
        await using var first = fixture.Services.CreateAsyncScope();
        await using var second = fixture.Services.CreateAsyncScope();
        var firstRun = first.ServiceProvider.GetRequiredService<IConversationsUnitOfWork>().RunLockedOnConversationAsync(
            _conversation.Id,
            async _ =>
            {
                firstInside.SetResult();
                await release.Task;
                return Result.Success();
            },
            Ct);
        await firstInside.Task;

        // Act
        var secondRun = second.ServiceProvider.GetRequiredService<IConversationsUnitOfWork>().RunLockedOnConversationAsync(
            _conversation.Id,
            _ =>
            {
                secondEntered = true;
                return Task.FromResult(Result.Success());
            },
            Ct);
        var waited = await fixture.WaitForAdvisoryLockWaiterAsync(TimeSpan.FromSeconds(10));
        var enteredWhileLocked = secondEntered;
        release.SetResult();
        await Task.WhenAll(firstRun, secondRun);

        // Assert
        waited.Should().BeTrue("a segunda requisição tem de ficar esperando a trava no banco");
        enteredWhileLocked.Should().BeFalse();
        secondEntered.Should().BeTrue("solta a trava, a segunda segue");
    }

    [Fact]
    public async Task RunLockedOnConversationAsync_Should_ReturnConflictWithoutThrowing_When_TheRetentionDeletedTheConversationMeanwhile()
    {
        // Arrange
        await using var scope = fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var messages = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IConversationsUnitOfWork>();
        var loaded = (await repository.GetByIdAsync(_conversation.Id, Ct))!;
        var message = ConversationBuilder.Text(loaded, ConversationParty.Owner, sentAt: ConversationBuilder.DefaultNow.AddMinutes(1));
        await fixture.ExecuteAsync($"DELETE FROM conversation.conversations WHERE id = '{_conversation.Id.Value}'");

        // Act
        var saved = await unitOfWork.RunLockedOnConversationAsync(
            _conversation.Id,
            async ct =>
            {
                await repository.ReloadAsync(loaded, ct);
                loaded.RecordMessage(message.CreatedAt);
                messages.Add(message);
                return await unitOfWork.SaveChangesAsync(ct);
            },
            Ct);

        // Assert
        saved.FirstError.Should().Be(ConversationsErrors.Conflict);
    }

    private async Task RecordInAnotherRequestAsync(DateTimeOffset sentAt)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var conversation = (await scope.ServiceProvider.GetRequiredService<IConversationRepository>().GetByIdAsync(_conversation.Id, Ct))!;
        conversation.RecordMessage(sentAt);
        (await scope.ServiceProvider.GetRequiredService<IConversationsUnitOfWork>().SaveChangesAsync(Ct)).IsSuccess.Should().BeTrue();
    }

    private async Task<DateTimeOffset?> LastMessageAtAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConversationsDbContext>().Conversations
            .AsNoTracking()
            .Where(c => c.Id == _conversation.Id)
            .Select(c => c.LastMessageAt)
            .SingleAsync(Ct);
    }
}
