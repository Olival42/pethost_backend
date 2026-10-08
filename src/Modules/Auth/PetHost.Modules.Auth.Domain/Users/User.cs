using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users.Events;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Conta do sistema: tutor, anfitrião ou admin. Espelha a tabela <c>users</c>
/// do dicionário de dados.
/// </summary>
/// <remarks>
/// A role é escolhida no cadastro e <b>não muda</b> — não existe método para alterá-la.
/// A mesma pessoa pode ter uma conta owner e uma host com o mesmo e-mail; a unicidade
/// é do par (e-mail, role), garantida pelo índice <c>uq_users_email_role</c>.
/// </remarks>
public sealed class User : Entity<UserId>
{
    public const int FullNameMaxLength = 120;
    public const int PhoneMaxLength = 20;
    public const int AvatarUrlMaxLength = 500;
    public const int NeighborhoodMaxLength = 80;
    public const int CityMaxLength = 80;
    public const int StateLength = 2;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private User()
    {
        FullName = null!;
        Email = null!;
        PasswordHash = null!;
    }

    private User(
        string fullName,
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

    public string FullName { get; private set; }
    public Email Email { get; private set; }
    public PasswordHash PasswordHash { get; private set; }
    public UserRole Role { get; private set; }

    public string? Phone { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? Neighborhood { get; private set; }
    public string? City { get; private set; }

    /// <summary>UF com duas letras, ex.: <c>PR</c>.</summary>
    public string? State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Cria uma conta de tutor ou anfitrião. Recusa <see cref="UserRole.Admin"/>:
    /// admin só existe pelo seed.
    /// </summary>
    public static Result<User> Register(
        string? fullName,
        Email email,
        PasswordHash passwordHash,
        UserRole role,
        DateTimeOffset now)
    {
        if (role is UserRole.Admin)
            return Result<User>.Failure(AuthErrors.AdminRegistrationForbidden);

        if (role is not (UserRole.Owner or UserRole.Host))
            return Result<User>.Failure(AuthErrors.RoleInvalid);

        var name = NormalizeFullName(fullName);
        if (name.IsFailure)
            return Result<User>.FromFailure(name);

        var user = new User(name.Value!, email, passwordHash, role, now);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Id, role, now));

        return Result<User>.Success(user);
    }

    /// <summary>
    /// Cria a conta de administrador. Uso exclusivo do seeder — o dicionário diz que
    /// o admin é criado direto no banco. Nenhum caso de uso da API chama este método.
    /// </summary>
    public static Result<User> CreateAdmin(
        string? fullName,
        Email email,
        PasswordHash passwordHash,
        DateTimeOffset now)
    {
        var name = NormalizeFullName(fullName);

        return name.IsFailure
            ? Result<User>.FromFailure(name)
            : Result<User>.Success(new User(name.Value!, email, passwordHash, UserRole.Admin, now));
    }

    /// <summary>Troca o hash da senha. Recebe hash, nunca senha em texto.</summary>
    public Result ChangePassword(PasswordHash newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = newPasswordHash;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>Atualiza os campos opcionais de perfil. <c>null</c> limpa o campo.</summary>
    public Result UpdateProfile(
        string? phone,
        string? avatarUrl,
        string? neighborhood,
        string? city,
        string? state,
        DateTimeOffset now)
    {
        Phone = Trim(phone);
        AvatarUrl = Trim(avatarUrl);
        Neighborhood = Trim(neighborhood);
        City = Trim(city);
        State = Trim(state)?.ToUpperInvariant();
        UpdatedAt = now;

        return Result.Success();
    }

    private static Result<string> NormalizeFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return Result<string>.Failure(AuthErrors.FullNameRequired);

        var normalized = fullName.Trim();

        return normalized.Length > FullNameMaxLength
            ? Result<string>.Failure(AuthErrors.FullNameTooLong)
            : Result<string>.Success(normalized);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
