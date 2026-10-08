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
    public const int NeighborhoodMaxLength = 80;
    public const int CityMaxLength = 80;

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

    public PhoneNumber? Phone { get; private set; }
    public AvatarUrl? AvatarUrl { get; private set; }

    /// <summary>
    /// Bairro e cidade ficam como texto: a única regra deles é o tamanho, então um
    /// value object não protegeria nada além do que a coluna já protege.
    /// </summary>
    public string? Neighborhood { get; private set; }

    public string? City { get; private set; }
    public StateCode? State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Cria uma conta de tutor ou anfitrião. Recusa <see cref="UserRole.Admin"/>:
    /// admin só existe pelo seed.
    /// </summary>
    public static Result<User> Register(
        FullName fullName,
        Email email,
        PasswordHash passwordHash,
        UserRole role,
        DateTimeOffset now)
    {
        if (role is UserRole.Admin)
            return Result<User>.Failure(AuthErrors.AdminRegistrationForbidden);

        if (role is not (UserRole.Owner or UserRole.Host))
            return Result<User>.Failure(AuthErrors.RoleInvalid);

        var user = new User(fullName, email, passwordHash, role, now);
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
    /// Atualiza os campos opcionais de perfil. <c>null</c> limpa o campo. Telefone,
    /// foto e UF chegam já validados pelos próprios value objects.
    /// </summary>
    public Result UpdateProfile(
        PhoneNumber? phone,
        AvatarUrl? avatarUrl,
        string? neighborhood,
        string? city,
        StateCode? state,
        DateTimeOffset now)
    {
        Phone = phone;
        AvatarUrl = avatarUrl;
        Neighborhood = Trim(neighborhood);
        City = Trim(city);
        State = state;
        UpdatedAt = now;

        return Result.Success();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
