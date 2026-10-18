using FluentAssertions;
using Moq;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Privacy;
using PetHost.Shared.Infrastructure.Privacy;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Privacy;

/// <summary>Exclusão da conta: cada módulo apaga a sua parte, um depois do outro.</summary>
public sealed class PersonalDataErasureTests
{
    private static readonly ErasedAccount Account = new(Guid.CreateVersion7(), Roles.Owner, Guid.CreateVersion7());

    private readonly Mock<IPersonalDataEraser> _pets = new();
    private readonly Mock<IPersonalDataEraser> _conversations = new();

    [Fact]
    public async Task EraseAsync_Should_CallEveryModuleInOrder_When_Called()
    {
        // Arrange
        var calls = new List<string>();
        _pets.Setup(e => e.EraseAsync(Account, It.IsAny<CancellationToken>())).Callback(() => calls.Add("pets")).Returns(Task.CompletedTask);
        _conversations.Setup(e => e.EraseAsync(Account, It.IsAny<CancellationToken>())).Callback(() => calls.Add("conversations")).Returns(Task.CompletedTask);

        // Act
        await new PersonalDataErasure([_pets.Object, _conversations.Object]).EraseAsync(Account, CancellationToken.None);

        // Assert
        calls.Should().Equal("pets", "conversations");
    }

    [Fact]
    public async Task EraseAsync_Should_StopAndThrow_When_AModuleFails()
    {
        // Arrange: a falha sobe (500) e o pedido pode ser repetido; o que vem depois não roda.
        _pets.Setup(e => e.EraseAsync(Account, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var act = () => new PersonalDataErasure([_pets.Object, _conversations.Object]).EraseAsync(Account, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _conversations.Verify(e => e.EraseAsync(It.IsAny<ErasedAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
