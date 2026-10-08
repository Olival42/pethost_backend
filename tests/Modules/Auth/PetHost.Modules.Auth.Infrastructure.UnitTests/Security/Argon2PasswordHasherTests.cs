using FluentAssertions;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Infrastructure.Security;
using Xunit;

namespace PetHost.Modules.Auth.Infrastructure.UnitTests.Security;

public sealed class Argon2PasswordHasherTests
{
    /// <summary>
    /// Custo mínimo nos testes: o Argon2 é lento de propósito e o default de
    /// 19 MiB deixaria a suíte arrastada. O algoritmo exercitado é o mesmo.
    /// </summary>
    private static readonly Argon2Options FastOptions = new()
    {
        MemorySizeKib = 1024,
        Iterations = 1,
        DegreeOfParallelism = 1,
    };

    private readonly Argon2PasswordHasher _sut = new(Options.Create(FastOptions));

    [Fact]
    public void Hash_Should_ProducePhcString_When_Called()
    {
        var hash = _sut.Hash("senha-forte-123");

        hash.Should().StartWith("$argon2id$v=19$m=1024,t=1,p=1$");
        hash.Split('$').Should().HaveCount(6);
    }

    [Fact]
    public void Hash_Should_FitTheDatabaseColumn_When_Called()
    {
        // password_hash e varchar(255) no dicionario de dados.
        var hash = _sut.Hash("senha-forte-123");

        hash.Length.Should().BeLessThanOrEqualTo(255);
    }

    [Fact]
    public void Hash_Should_ProduceDifferentResults_When_SamePasswordIsHashedTwice()
    {
        // Salt aleatorio por hash: duas contas com a mesma senha nao tem o mesmo
        // hash, o que inviabiliza rainbow table e comparacao entre usuarios.
        var first = _sut.Hash("mesma-senha");
        var second = _sut.Hash("mesma-senha");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Verify_Should_ReturnTrue_When_PasswordMatches()
    {
        var hash = _sut.Hash("senha-forte-123");

        _sut.Verify("senha-forte-123", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_Should_ReturnFalse_When_PasswordDiffers()
    {
        var hash = _sut.Hash("senha-forte-123");

        _sut.Verify("senha-forte-124", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_BeCaseSensitive_When_PasswordDiffersOnlyInCasing()
    {
        var hash = _sut.Hash("SenhaForte123");

        _sut.Verify("senhaforte123", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_ReturnFalse_When_PasswordIsEmptyAgainstRealHash()
    {
        var hash = _sut.Hash("senha-forte-123");

        _sut.Verify(string.Empty, hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_RoundTrip_When_PasswordHasUnicode()
    {
        var hash = _sut.Hash("sênha-cõm-acênto-日本語");

        _sut.Verify("sênha-cõm-acênto-日本語", hash).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nao-e-um-hash")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$apenas-quatro-partes")]
    [InlineData("$bcrypt$v=19$m=1024,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=16$m=1024,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=abc,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=1024,t=1,p=1$!!!nao-base64!!!$aGFzaA")]
    public void Verify_Should_ReturnFalse_When_StoredHashIsMalformed(string storedHash)
    {
        // Hash corrompido nunca autentica, e nunca estoura: um registro ruim no
        // banco viraria 500 em vez de 401.
        _sut.Verify("qualquer-senha", storedHash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_StillWork_When_HashWasCreatedWithDifferentCost()
    {
        // O hash guarda os proprios parametros, entao subir o custo nao invalida
        // as senhas ja cadastradas.
        var cheapHasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemorySizeKib = 1024,
            Iterations = 1,
            DegreeOfParallelism = 1,
        }));

        var expensiveHasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemorySizeKib = 2048,
            Iterations = 2,
            DegreeOfParallelism = 1,
        }));

        var oldHash = cheapHasher.Hash("senha-antiga");

        expensiveHasher.Verify("senha-antiga", oldHash).Should().BeTrue();
    }

    [Fact]
    public void Hash_Should_UseConfiguredCost_When_OptionsChange()
    {
        var hasher = new Argon2PasswordHasher(Options.Create(new Argon2Options
        {
            MemorySizeKib = 2048,
            Iterations = 3,
            DegreeOfParallelism = 2,
        }));

        hasher.Hash("senha").Should().Contain("m=2048,t=3,p=2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hash_Should_Throw_When_PasswordIsEmpty(string? password)
    {
        // Senha vazia viola a regra de Password. Falhar alto aqui evita gravar no
        // banco o hash de uma senha vazia por causa de um bug em outra camada.
        var act = () => _sut.Hash(password!);

        act.Should().Throw<ArgumentException>();
    }
}
