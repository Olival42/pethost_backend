using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users.Events;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Conta do sistema: tutor, anfitrião ou admin. Espelha a tabela <c>users</c>
/// do dicionário de dados — é a base comum; o que é só do tutor (CPF,
/// Customer do Stripe) fica no módulo Owners.
/// </summary>
/// <remarks>
/// A role é escolhida no cadastro e <b>não muda</b> — não existe método para alterá-la.
/// A mesma pessoa pode ter uma conta owner e uma host com o mesmo e-mail; a unicidade
/// é do par (e-mail, role), garantida pelo índice <c>uq_users_email_role</c>.
/// </remarks>
public sealed class User : Entity<UserId>
{
    /// <summary>Quem contrata e paga precisa ser maior de idade (o Stripe exige o mesmo do anfitrião).</summary>
    public const int MinimumAge = 18;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private User()
    {
        FullName = null!;
        Email = null!;
        PasswordHash = null!;
    }

    private User(
        FullName fullName,
        Email email,
        PasswordHash passwordHash,
        UserRole role,
        DateTimeOffset now)
        : base(UserId.New())
    {
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public FullName FullName { get; private set; }
    public Email Email { get; private set; }
    public PasswordHash PasswordHash { get; private set; }
    public UserRole Role { get; private set; }

    /// <summary>Obrigatório para tutor e anfitrião; nulo só no admin.</summary>
    public PhoneNumber? Phone { get; private set; }

    public AvatarUrl? AvatarUrl { get; private set; }

    /// <summary>Obrigatório para tutor e anfitrião; nulo só no admin.</summary>
    public DateOnly? BirthDate { get; private set; }

    /// <summary>Obrigatório para tutor e anfitrião; nulo só no admin.</summary>
    public Address? Address { get; private set; }

    /// <summary>
    /// Conta ativa. Inativar vale só para esta conta: quem tem conta de tutor e de
    /// anfitrião pode inativar uma e seguir usando a outra.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Quando a conta foi inativada. Nulo enquanto está ativa.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>Tamanho máximo do motivo da suspensão.</summary>
    public const int SuspensionReasonMaxLength = 500;

    /// <summary>
    /// Quando o admin suspendeu a conta. Diferente de inativar: a pessoa <b>não</b> tira a
    /// suspensão sozinha (nem com a senha); só o admin.
    /// </summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>Motivo informado pelo admin. Nulo enquanto não suspensa.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>Id do admin que suspendeu.</summary>
    public Guid? SuspendedBy { get; private set; }

    public bool IsSuspended => SuspendedAt is not null;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Cria uma conta de tutor ou anfitrião. Recusa <see cref="UserRole.Admin"/>
    /// (admin só existe pelo seed) e quem tem menos de <see cref="MinimumAge"/> anos.
    /// </summary>
    public static Result<User> Register(
        FullName fullName,
        Email email,
        PasswordHash passwordHash,
        UserRole role,
        PhoneNumber phone,
        DateOnly birthDate,
        Address address,
        DateTimeOffset now)
    {
        if (role is UserRole.Admin)
            return Result<User>.Failure(AuthErrors.AdminRegistrationForbidden);

        if (role is not (UserRole.Owner or UserRole.Host))
            return Result<User>.Failure(AuthErrors.RoleInvalid);

        var birthDateError = ValidateBirthDate(birthDate, now);
        if (birthDateError is not null)
            return Result<User>.Failure(birthDateError);

        var user = new User(fullName, email, passwordHash, role, now)
        {
            Phone = phone,
            BirthDate = birthDate,
            Address = address,
        };

        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, role, now));

        return Result<User>.Success(user);
    }

    /// <summary>
    /// Cria a conta de administrador. Uso exclusivo do seeder — o dicionário diz que
    /// o admin é criado direto no banco. Nenhum caso de uso da API chama este método.
    /// </summary>
    public static Result<User> CreateAdmin(
        FullName fullName,
        Email email,
        PasswordHash passwordHash,
        DateTimeOffset now) =>
        Result<User>.Success(new User(fullName, email, passwordHash, UserRole.Admin, now));

    /// <summary>Troca o hash da senha. Recebe hash, nunca senha em texto.</summary>
    public Result ChangePassword(PasswordHash newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = newPasswordHash;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Atualiza os dados de perfil. Tudo chega já validado pelos próprios value
    /// objects; a foto é a única que pode ser limpa (<c>null</c>). E-mail e papel não
    /// mudam por aqui; o nascimento muda em <see cref="ChangeBirthDate"/>. Valores
    /// iguais aos atuais não contam como alteração: <see cref="UpdatedAt"/> só anda se
    /// algo mudou de fato.
    /// </summary>
    public Result UpdateProfile(
        FullName fullName,
        PhoneNumber phone,
        AvatarUrl? avatarUrl,
        Address address,
        DateTimeOffset now)
    {
        var changed = fullName != FullName || phone != Phone || avatarUrl != AvatarUrl || address != Address;
        if (!changed)
            return Result.Success();

        FullName = fullName;
        Phone = phone;
        AvatarUrl = avatarUrl;
        Address = address;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Corrige a data de nascimento, com as mesmas regras do cadastro (não futura,
    /// <see cref="MinimumAge"/> anos ou mais). Quando a troca é permitida — o tutor,
    /// por exemplo, só até o primeiro pagamento — é decisão de quem chama. A mesma data
    /// de antes não conta como alteração.
    /// </summary>
    public Result ChangeBirthDate(DateOnly birthDate, DateTimeOffset now)
    {
        var error = ValidateBirthDate(birthDate, now);
        if (error is not null)
            return Result.Failure(error);

        if (birthDate == BirthDate)
            return Result.Success();

        BirthDate = birthDate;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Inativa a conta. O admin não pode ser inativado: o seed não o recriaria e o
    /// sistema ficaria sem administrador.
    /// </summary>
    public Result Deactivate(DateTimeOffset now)
    {
        if (Role is UserRole.Admin)
            return Result.Failure(AuthErrors.AdminDeactivationForbidden);

        // Idempotente: inativar uma conta já inativa não muda nada.
        if (!IsActive)
            return Result.Success();

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Suspende a conta (ação do admin, com motivo). Quem chama derruba as sessões.
    /// Suspender de novo uma conta já suspensa não muda nada — vale a primeira.
    /// </summary>
    public Result Suspend(string? reason, Guid adminId, DateTimeOffset now)
    {
        if (Role is UserRole.Admin)
            return Result.Failure(AuthErrors.AdminSuspensionForbidden);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(AuthErrors.SuspensionReasonRequired);

        if (reason.Trim().Length > SuspensionReasonMaxLength)
            return Result.Failure(AuthErrors.SuspensionReasonTooLong);

        if (IsSuspended)
            return Result.Success();

        SuspendedAt = now;
        SuspensionReason = reason.Trim();
        SuspendedBy = adminId;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Tira a suspensão. Idempotente. O status de ativa/inativa não muda.</summary>
    public Result LiftSuspension(DateTimeOffset now)
    {
        if (!IsSuspended)
            return Result.Success();

        SuspendedAt = null;
        SuspensionReason = null;
        SuspendedBy = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Reativa a conta. Idempotente: reativar uma conta ativa não muda nada.</summary>
    public Result Reactivate(DateTimeOffset now)
    {
        if (IsActive)
            return Result.Success();

        IsActive = true;
        DeactivatedAt = null;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Idade em anos completos no dia de <paramref name="now"/> (UTC).</summary>
    public static int AgeAt(DateOnly birthDate, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var age = today.Year - birthDate.Year;

        return birthDate > today.AddYears(-age) ? age - 1 : age;
    }

    /// <summary>Regra da data de nascimento: não futura e 18+. <c>null</c> se válida.</summary>
    public static Error? ValidateBirthDate(DateOnly birthDate, DateTimeOffset now)
    {
        if (birthDate > DateOnly.FromDateTime(now.UtcDateTime))
            return AuthErrors.BirthDateInFuture;

        return AgeAt(birthDate, now) < MinimumAge ? AuthErrors.Underage : null;
    }
}
