using FluentAssertions;
using PetHost.Modules.Auth.Application.Users;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Users;

public sealed class ProfilePatchTests
{
    private static readonly DateTimeOffset Now = UserBuilder.DefaultNow;

    private readonly User _user = new UserBuilder().WithFullName("Camila Souza").Build();

    [Fact]
    public void Apply_Should_KeepEverything_When_NothingIsSent()
    {
        var result = ProfilePatch.Apply(_user, Patch(), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FullName.Should().Be(_user.FullName);
        result.Value.Phone.Should().Be(_user.Phone);
        result.Value.Address.Should().Be(_user.Address);
        result.Value.BirthDate.Should().BeNull("data não veio, não muda");
    }

    [Fact]
    public void Apply_Should_MergeAddressFieldByField_When_AddressIsPartial()
    {
        var result = ProfilePatch.Apply(_user, Patch(address: new AddressData(null, null, "300", null, null, null, null)), Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Address.Number.Should().Be("300");
        result.Value.Address.Street.Should().Be("Rua das Flores");
        result.Value.Address.Complement.Should().Be("Apto 3", "complemento ausente fica como estava");
    }

    [Fact]
    public void Apply_Should_ClearOptionalFields_When_EmptyTextIsSent()
    {
        var withAvatar = ProfilePatch.Apply(_user, Patch(avatarUrl: "https://cdn.pethost.com/a.png"), Now).Value!;
        _user.UpdateProfile(withAvatar.FullName, withAvatar.Phone, withAvatar.AvatarUrl, withAvatar.Address, Now);

        var result = ProfilePatch.Apply(_user, Patch(avatarUrl: "", address: new AddressData(null, null, null, "", null, null, null)), Now);

        result.Value!.AvatarUrl.Should().BeNull();
        result.Value.Address.Complement.Should().BeNull();
    }

    [Fact]
    public void Apply_Should_ParseBirthDate_When_Sent()
    {
        var result = ProfilePatch.Apply(_user, Patch(birthDate: "1991-02-03"), Now);

        result.Value!.BirthDate.Should().Be(new DateOnly(1991, 2, 3));
    }

    [Theory]
    [InlineData("", "Birth date is required.")]
    [InlineData("03/02/1991", "Birth date must be a valid date in the format yyyy-MM-dd.")]
    [InlineData("2099-01-01", "Birth date cannot be in the future.")]
    [InlineData("2015-01-01", "You must be at least 18 years old.")]
    public void Apply_Should_RejectBirthDate_When_ItBreaksTheRegistrationRules(string birthDate, string message)
    {
        var result = ProfilePatch.Apply(_user, Patch(birthDate: birthDate), Now);

        result.Errors!.Should().ContainSingle(e => e.Field == "birthDate" && e.Message == message);
    }

    [Fact]
    public void Apply_Should_ReturnEveryError_When_ManyFieldsAreInvalid()
    {
        var result = ProfilePatch.Apply(
            _user,
            Patch("", "abc", "javascript:alert(1)", new AddressData("1", null, null, null, null, null, "XX"), "x"),
            Now);

        result.Errors!.Select(e => e.Field).Should()
            .Contain(["fullName", "phone", "avatarUrl", "address.zipCode", "address.state", "birthDate"]);
    }

    [Fact]
    public void Snapshot_Should_UseEmptyTextForMissingOptionals_So_ApplyingItBackClearsThem()
    {
        var snapshot = ProfilePatch.Snapshot(_user);

        snapshot.AvatarUrl.Should().Be("");
        snapshot.FullName.Should().Be("Camila Souza");
        snapshot.Address!.Complement.Should().Be("Apto 3");
        snapshot.BirthDate.Should().Be("1990-05-10");
    }

    private static AccountProfilePatch Patch(
        string? fullName = null,
        string? phone = null,
        string? avatarUrl = null,
        AddressData? address = null,
        string? birthDate = null) =>
        new(fullName, phone, avatarUrl, address, birthDate);
}
