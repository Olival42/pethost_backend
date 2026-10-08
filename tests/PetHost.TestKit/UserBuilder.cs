using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.TestKit;

/// <summary>
/// Monta <see cref="User"/> para teste. Cada <c>With...</c> troca um campo;
/// o resto fica no default válido, para o teste declarar só o que importa.
/// </summary>
public sealed class UserBuilder
{
    /// <summary>Hash em formato PHC válido. Conteúdo irrelevante fora dos testes de hash.</summary>
    public const string SampleHash =
        "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoMDA";

    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private string? _fullName = "Camila Souza";
    private string _email = "camila@exemplo.com";
    private string _passwordHash = SampleHash;
    private UserRole _role = UserRole.Owner;
    private DateTimeOffset _now = DefaultNow;
    private Guid? _id;

    public UserBuilder WithFullName(string? fullName)
    {
        _fullName = fullName;
        return this;
    }

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder WithPasswordHash(string passwordHash)
    {
        _passwordHash = passwordHash;
        return this;
    }

    public UserBuilder WithRole(UserRole role)
    {
        _role = role;
        return this;
    }

    public UserBuilder WithCreatedAt(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    /// <summary>
    /// Fixa o id em vez do UUID v7 aleatório que o agregado gera, para o teste
    /// poder comparar com um valor conhecido (ver <see cref="TestIds"/>).
    /// </summary>
    public UserBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public User Build()
    {
        var email = Email.Create(_email);
        if (email.IsFailure)
            throw new InvalidOperationException($"Invalid test email '{_email}'.");

        var hash = PasswordHash.FromHash(_passwordHash);
        if (hash.IsFailure)
            throw new InvalidOperationException("Invalid test password hash.");

        var user = _role is UserRole.Admin
            ? User.CreateAdmin(_fullName, email.Value!, hash.Value!, _now)
            : User.Register(_fullName, email.Value!, hash.Value!, _role, _now);

        if (user.IsFailure)
            throw new InvalidOperationException($"Could not build test user: {user.FirstError?.Code}.");

        if (_id is { } id)
            AssignId(user.Value!, id);

        return user.Value!;
    }

    /// <summary>
    /// O setter de <c>Id</c> é <c>protected</c> de propósito — só o EF Core deveria
    /// escrever nele. Nos testes usamos reflexão em vez de abrir a porta no domínio.
    /// </summary>
    private static void AssignId(User user, Guid id)
    {
        var property = typeof(User).GetProperty(nameof(User.Id))
            ?? throw new InvalidOperationException("User.Id not found.");

        property.SetValue(user, new UserId(id));
    }
}
