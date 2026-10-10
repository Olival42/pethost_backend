using FluentAssertions;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;
using PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

public sealed class ChangeMyAvatarCommandHandlerTests
{
    private const string NewUrl = "https://img.pethost.test/avatars/new.png";
    private const string NewHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
    private const string OldUrl = "https://img.pethost.test/avatars/old.png";

    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IImageStorage> _storage = new();
    private readonly Owner _owner = Owner.Create(UserId, Cpf.Create("52998224725").Value!, Accounts.Now);
    private readonly ImageUpload _image = new(new MemoryStream([1]), "eu.png", "image/png");
    private readonly ChangeMyAvatarCommandHandler _sut;

    public ChangeMyAvatarCommandHandlerTests()
    {
        _ownerRepository.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(UserId, "Camila") with { AvatarUrl = NewUrl }]);
        _storage
            .Setup(s => s.SaveAsync(_image, ImageFolders.Avatars, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Success(new StoredImage(NewUrl, NewHash)));

        _sut = new ChangeMyAvatarCommandHandler(_ownerRepository.Object, _ownerAccounts.Object, _storage.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveTheUrlOnTheAccountAndDeleteTheOldAvatar_When_ImageIsValid()
    {
        // Arrange
        AccountProfilePatch? sent = null;
        _ownerAccounts
            .Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, AccountProfilePatch, CancellationToken>((_, patch, _) => sent = patch)
            .ReturnsAsync(Result<AccountProfilePatch>.Success(Previous(OldUrl)));

        // Act
        var result = await _sut.HandleAsync(new ChangeMyAvatarCommand(UserId, _image), CancellationToken.None);

        // Assert
        result.Value!.User!.AvatarUrl.Should().Be(NewUrl);
        sent!.AvatarUrl.Should().Be(NewUrl);
        sent.FullName.Should().BeNull("só a foto muda");
        _storage.Verify(s => s.DeleteAsync(OldUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteNothingOld_When_AccountHadNoAvatar()
    {
        // Arrange — o editor devolve texto vazio quando não havia foto.
        _ownerAccounts
            .Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountProfilePatch>.Success(Previous(string.Empty)));

        // Act
        await _sut.HandleAsync(new ChangeMyAvatarCommand(UserId, _image), CancellationToken.None);

        // Assert
        _storage.Verify(s => s.DeleteAsync(It.Is<string?>(u => !string.IsNullOrEmpty(u)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheNewImage_When_TheAccountRejectsTheUrl()
    {
        // Arrange
        _ownerAccounts
            .Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountProfilePatch>.Failure(new Error("AUTH_ACCOUNT_NOT_FOUND", "Gone.")));

        // Act
        var result = await _sut.HandleAsync(new ChangeMyAvatarCommand(UserId, _image), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _storage.Verify(s => s.DeleteAsync(NewUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotUploadAnything_When_AccountHasNoOwnerProfile()
    {
        // Act
        var result = await _sut.HandleAsync(new ChangeMyAvatarCommand(Guid.CreateVersion7(), _image), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    internal static AccountProfilePatch Previous(string avatarUrl) =>
        new("Camila", "44999990000", avatarUrl, Address: null);
}

public sealed class RemoveMyAvatarCommandHandlerTests
{
    private const string OldUrl = "https://img.pethost.test/avatars/old.png";

    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IImageStorage> _storage = new();
    private readonly RemoveMyAvatarCommandHandler _sut;

    public RemoveMyAvatarCommandHandlerTests()
    {
        var ownerRepository = new Mock<IOwnerRepository>();
        ownerRepository
            .Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Owner.Create(UserId, Cpf.Create("52998224725").Value!, Accounts.Now));
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(UserId, "Camila")]);

        _sut = new RemoveMyAvatarCommandHandler(ownerRepository.Object, _ownerAccounts.Object, _storage.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ClearTheAvatarWithEmptyTextAndDeleteTheImage_When_AccountHasAvatar()
    {
        // Arrange
        AccountProfilePatch? sent = null;
        _ownerAccounts
            .Setup(a => a.UpdateProfileAsync(UserId, It.IsAny<AccountProfilePatch>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, AccountProfilePatch, CancellationToken>((_, patch, _) => sent = patch)
            .ReturnsAsync(Result<AccountProfilePatch>.Success(ChangeMyAvatarCommandHandlerTests.Previous(OldUrl)));

        // Act
        var result = await _sut.HandleAsync(new RemoveMyAvatarCommand(UserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        sent!.AvatarUrl.Should().Be(string.Empty, "texto vazio limpa a foto no patch de perfil");
        _storage.Verify(s => s.DeleteAsync(OldUrl, It.IsAny<CancellationToken>()), Times.Once);
    }
}
