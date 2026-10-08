namespace PetHost.Modules.Auth.Domain.Users;

/// <summary>
/// Identidade de <see cref="User"/>. Envolve o <c>uuid</c> da coluna <c>users.id</c>
/// para que um id de usuário não seja confundido com qualquer outro <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// Gerado pelo domínio, não pelo banco: o agregado nasce com id e pode ser referenciado
/// (token, evento, Redis) antes do insert. UUID v7 é ordenado por tempo, então o índice
/// da chave primária cresce no fim em vez de se fragmentar como com UUID v4.
/// </remarks>
/// <param name="Value">Valor da coluna <c>users.id</c>.</param>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
