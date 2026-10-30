using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Domain.Users.Events;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Email AnyEmail => Email.Create("camila@exemplo.com").Value!;

    private static PasswordHash AnyHash => PasswordHash.FromHash(UserBuilder.SampleHash).Value!;

    private static FullName Name(string value) => FullName.Create(value).Value!;

    private static PhoneNumber AnyPhone => PhoneNumber.Create("44 99999-0000").Value!;

    private static readonly DateOnly Adult = new(1990, 5, 10);

    private static Result<User> Register(UserRole role, DateOnly? birthDate = null) =>
        User.Register(Name("Camila Souza"), AnyEmail, AnyHash, role, AnyPhone, birthDate ?? Adult, UserBuilder.DefaultAddress(), Now);

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Host)]
    public void Register_Should_Succeed_When_RoleIsOwnerOrHost(UserRole role)
    {
        var result = Register(role);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(role);
        result.Value.CreatedAt.Should().Be(Now);
        result.Value.UpdatedAt.Should().Be(Now);
        result.Value.Phone!.Value.Should().Be("44999990000");
        result.Value.BirthDate.Should().Be(Adult);
        result.Value.Address.Should().Be(UserBuilder.DefaultAddress());
    }

    [Fact]
    public void Register_Should_Fail_When_RoleIsAdmin()
    {
        var result = Register(UserRole.Admin);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_ADMIN_REGISTRATION_FORBIDDEN");
    }

    [Fact]
    public void Register_Should_Fail_When_RoleIsNotAKnownValue()
    {
        var result = Register((UserRole)99);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("role");
    }

    [Fact]
    public void Register_Should_StartActive_When_Succeeds()
    {
        var user = Register(UserRole.Owner).Value!;

        user.IsActive.Should().BeTrue();
        user.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    public void Deactivate_Should_MarkInactiveAndTouchUpdatedAt_When_AccountIsActive()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        var later = Now.AddDays(3);

        var result = user.Deactivate(later);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.DeactivatedAt.Should().Be(later);
        user.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void Deactivate_Should_KeepFirstDate_When_CalledTwice()
    {
        var user = new UserBuilder().Build();
        user.Deactivate(Now);

        user.Deactivate(Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.DeactivatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivate_Should_Fail_When_AccountIsAdmin()
    {
        var admin = new UserBuilder().WithRole(UserRole.Admin).Build();

        admin.Deactivate(Now).FirstError!.Code.Should().Be("AUTH_ADMIN_DEACTIVATION_FORBIDDEN");
        admin.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Reactivate_Should_ClearDeactivation_When_AccountIsInactive()
    {
        var user = new UserBuilder().Build();
        user.Deactivate(Now);

        user.Reactivate(Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.IsActive.Should().BeTrue();
        user.DeactivatedAt.Should().BeNull();
        user.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Register_Should_Succeed_When_PersonTurns18Today()
    {
        var eighteenToday = DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-User.MinimumAge);

        Register(UserRole.Owner, eighteenToday).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Register_Should_FailWithUnderage_When_PersonTurns18Tomorrow()
    {
        var eighteenTomorrow = DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-User.MinimumAge).AddDays(1);

        var result = Register(UserRole.Owner, eighteenTomorrow);

        result.FirstError!.Field.Should().Be("birthDate");
        result.FirstError.Message.Should().Contain("18");
    }

    [Fact]
    public void Register_Should_Fail_When_BirthDateIsInTheFuture()
    {
        var result = Register(UserRole.Owner, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1));

        result.FirstError!.Message.Should().Contain("future");
    }

    [Fact]
    public void Register_Should_RaiseUserRegisteredDomainEvent_When_Succeeds()
    {
        var result = Register(UserRole.Host);

        result.Value!.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserRegisteredDomainEvent>()
            .Which.Role.Should().Be(UserRole.Host);
    }

    [Fact]
    public void CreateAdmin_Should_Succeed_When_CalledBySeeder()
    {
        var result = User.CreateAdmin(Name("PetHost Admin"), AnyEmail, AnyHash, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void CreateAdmin_Should_NotRaiseDomainEvent_When_Seeding()
    {
        var result = User.CreateAdmin(Name("PetHost Admin"), AnyEmail, AnyHash, Now);

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
    public void UpdateProfile_Should_ReplaceFieldsAndTouchUpdatedAt_When_Called()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        var later = Now.AddDays(1);
        var newAddress = Address.Create("87013-000", "Av. Brasil", "S/N", null, "Centro", "Maringá", "pr").Value!;

        user.UpdateProfile(
            Name(" Camila S. Souza "),
            PhoneNumber.Create("(44) 3222-1111").Value!,
            AvatarUrl.Create("https://cdn.pethost.com/a.png").Value,
            newAddress,
            later);

        user.FullName.Value.Should().Be("Camila S. Souza");
        user.Phone!.Value.Should().Be("4432221111");
        user.AvatarUrl!.Value.Should().Be("https://cdn.pethost.com/a.png");
        user.Address.Should().Be(newAddress);
        user.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void UpdateProfile_Should_ClearAvatar_When_AvatarIsNull()
    {
        var user = new UserBuilder().Build();
        user.UpdateProfile(user.FullName, user.Phone!, AvatarUrl.Create("https://cdn.pethost.com/a.png").Value, user.Address!, Now);

        user.UpdateProfile(user.FullName, user.Phone!, null, user.Address!, Now);

        user.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public void CreateAdmin_Should_LeaveProfileEmpty_When_Seeding()
    {
        var admin = User.CreateAdmin(Name("PetHost Admin"), AnyEmail, AnyHash, Now).Value!;

        admin.Phone.Should().BeNull();
        admin.BirthDate.Should().BeNull();
        admin.Address.Should().BeNull();
        admin.IsActive.Should().BeTrue();
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

    [Fact]
    public void UpdateProfile_Should_NotTouchUpdatedAt_When_ValuesAreTheSame()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();

        user.UpdateProfile(user.FullName, user.Phone!, user.AvatarUrl, user.Address!, Now.AddDays(1));

        user.UpdatedAt.Should().Be(Now, "valor igual ao atual não é alteração");
    }

    [Fact]
    public void ChangeBirthDate_Should_ReplaceTheDate_When_ValidAndDifferent()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();

        user.ChangeBirthDate(new DateOnly(1991, 2, 3), Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.BirthDate.Should().Be(new DateOnly(1991, 2, 3));
        user.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void ChangeBirthDate_Should_KeepTheRules_When_PersonWouldBeUnder18()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();

        var result = user.ChangeBirthDate(DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-17), Now);

        result.FirstError!.Field.Should().Be("birthDate");
        user.BirthDate.Should().Be(new DateOnly(1990, 5, 10));
    }

    [Fact]
    public void ChangeBirthDate_Should_NotTouchUpdatedAt_When_DateIsTheSame()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();

        user.ChangeBirthDate(new DateOnly(1990, 5, 10), Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.UpdatedAt.Should().Be(Now);
    }

    // ----- suspensão (admin) -----

    [Fact]
    public void Suspend_Should_RecordReasonAndAdmin_When_Called()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        var admin = Guid.CreateVersion7();

        user.Suspend("  fraude de CPF  ", admin, Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.IsSuspended.Should().BeTrue();
        user.SuspendedAt.Should().Be(Now.AddDays(1));
        user.SuspensionReason.Should().Be("fraude de CPF");
        user.SuspendedBy.Should().Be(admin);
        user.IsActive.Should().BeTrue("suspender não mexe no status de ativa/inativa");
    }

    [Fact]
    public void Suspend_Should_KeepTheFirstSuspension_When_CalledAgain()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        user.Suspend("primeiro", Guid.CreateVersion7(), Now);

        user.Suspend("segundo", Guid.CreateVersion7(), Now.AddDays(1));

        user.SuspensionReason.Should().Be("primeiro");
        user.SuspendedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Suspend_Should_RequireAReason_When_ReasonIsMissing(string? reason)
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();

        user.Suspend(reason, Guid.CreateVersion7(), Now).FirstError!.Field.Should().Be("reason");
        user.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public void Suspend_Should_Fail_When_AccountIsAdmin()
    {
        var admin = new UserBuilder().WithRole(UserRole.Admin).WithCreatedAt(Now).Build();

        admin.Suspend("x", Guid.CreateVersion7(), Now).FirstError!.Code.Should().Be("AUTH_ADMIN_SUSPENSION_FORBIDDEN");
    }

    [Fact]
    public void LiftSuspension_Should_ClearEverything_When_Suspended()
    {
        var user = new UserBuilder().WithCreatedAt(Now).Build();
        user.Suspend("fraude", Guid.CreateVersion7(), Now);

        user.LiftSuspension(Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.IsSuspended.Should().BeFalse();
        user.SuspensionReason.Should().BeNull();
        user.SuspendedBy.Should().BeNull();
    }
}
