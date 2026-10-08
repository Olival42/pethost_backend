using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Domain.Users.Events;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Email AnyEmail => Email.Create("camila@exemplo.com").Value!;

    private static PasswordHash AnyHash => PasswordHash.FromHash(UserBuilder.SampleHash).Value!;

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Host)]
    public void Register_Should_Succeed_When_RoleIsOwnerOrHost(UserRole role)
    {
        var result = User.Register("Camila Souza", AnyEmail, AnyHash, role, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(role);
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Register_Should_Fail_When_RoleIsAdmin()
    {
        var result = User.Register("Admin", AnyEmail, AnyHash, UserRole.Admin, Now);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_ADMIN_REGISTRATION_FORBIDDEN");
    }

    [Fact]
    public void Register_Should_Fail_When_RoleIsNotAKnownValue()
    {
        var result = User.Register("Camila", AnyEmail, AnyHash, (UserRole)99, Now);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("role");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Register_Should_Fail_When_FullNameIsMissing(string? fullName)
    {
        var result = User.Register(fullName, AnyEmail, AnyHash, UserRole.Owner, Now);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("fullName");
    }

    [Fact]
    public void Register_Should_Fail_When_FullNameExceedsMaxLength()
    {
        var tooLong = new string('a', User.FullNameMaxLength + 1);

        var result = User.Register(tooLong, AnyEmail, AnyHash, UserRole.Owner, Now);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("fullName");
    }

    [Fact]
    public void Register_Should_TrimFullName_When_ValueHasSurroundingSpaces()
    {
        var result = User.Register("  Camila Souza  ", AnyEmail, AnyHash, UserRole.Owner, Now);

        result.Value!.FullName.Should().Be("Camila Souza");
    }

    [Fact]
    public void Register_Should_RaiseUserRegisteredDomainEvent_When_Succeeds()
    {
        var result = User.Register("Camila Souza", AnyEmail, AnyHash, UserRole.Host, Now);

        result.Value!.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>()
            .Which.Role.Should().Be(UserRole.Host);
    }

    [Fact]
    public void CreateAdmin_Should_Succeed_When_CalledBySeeder()
    {
        var result = User.CreateAdmin("PetHost Admin", AnyEmail, AnyHash, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void CreateAdmin_Should_NotRaiseDomainEvent_When_Seeding()
    {
        var result = User.CreateAdmin("PetHost Admin", AnyEmail, AnyHash, Now);

        result.Value!.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangePassword_Should_ReplaceHashAndTouchUpdatedAt_When_Called()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        var newHash = PasswordHash.FromHash("$argon2id$v=19$m=19456,t=2,p=1$bm92b3NhbHQ$bm92b2hhc2g").Value!;
        var later = Now.AddHours(3);

        var result = user.ChangePassword(newHash, later);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be(newHash);
        user.UpdatedAt.Should().Be(later);
        user.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void UpdateProfile_Should_NormalizeFields_When_Called()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        var later = Now.AddDays(1);

        user.UpdateProfile("  44 99999-0000 ", null, " Jardim Alvorada ", " Maringá ", "pr", later);

        user.Phone.Should().Be("44 99999-0000");
        user.AvatarUrl.Should().BeNull();
        user.Neighborhood.Should().Be("Jardim Alvorada");
        user.City.Should().Be("Maringá");
        user.State.Should().Be("PR");
        user.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void UpdateProfile_Should_ClearField_When_ValueIsBlank()
    {
        var user = new UserBuilder().Build();
        user.UpdateProfile("44 99999-0000", null, "Zona 7", "Maringá", "PR", Now);

        user.UpdateProfile("   ", null, null, null, null, Now);

        user.Phone.Should().BeNull();
        user.Neighborhood.Should().BeNull();
    }

    [Fact]
    public void Role_Should_NotBeChangeable_When_InspectedByReflection()
    {
        // O escopo diz que a role e escolhida no cadastro e nao muda: o setter
        // nao pode ser publico e nao pode existir metodo publico que a altere.
        var setter = typeof(User).GetProperty(nameof(User.Role))!.SetMethod;

        setter.Should().NotBeNull();
        setter!.IsPublic.Should().BeFalse();

        var publicMutators = typeof(User).GetMethods()
            .Where(m => m.IsPublic
                        && !m.IsSpecialName
                        && m.Name.Contains("Role", StringComparison.Ordinal));

        publicMutators.Should().BeEmpty();
    }
}
