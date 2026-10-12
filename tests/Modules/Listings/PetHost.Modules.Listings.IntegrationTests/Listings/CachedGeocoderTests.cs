using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Geocoding;
using StackExchange.Redis;
using Xunit;

namespace PetHost.Modules.Listings.IntegrationTests.Listings;

/// <summary>
/// Cache do geocodificador no Redis (container real): achou e não achou ficam guardados com
/// TTL, falha do mapa não fica, e o endereço não vai em texto para a chave.
/// </summary>
[Collection(ListingsApiCollection.Name)]
public sealed class CachedGeocoderTests(ListingsApiFixture fixture) : IAsyncLifetime
{
    private readonly CountingGeocoder _inner = new();

    // Rua única por teste: o Redis é compartilhado pela coleção e não é limpo entre testes.
    private readonly string _street = $"Rua das Flores {Guid.CreateVersion7():N}";
    private IConnectionMultiplexer _redis = null!;
    private CachedGeocoder _sut = null!;

    public ValueTask InitializeAsync()
    {
        _redis = fixture.Services.GetRequiredService<IConnectionMultiplexer>();
        _sut = new CachedGeocoder(
            _inner,
            _redis,
            Options.Create(new GeocodingOptions { CacheHours = 2, NotFoundCacheMinutes = 5 }),
            NullLogger<CachedGeocoder>.Instance);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LocateAsync_Should_CallTheMapOnce_When_TheSameAddressIsAskedTwice()
    {
        // Arrange
        _inner.Next = GeocodingResult.Found(-23.425269m, -51.938604m);

        // Act
        var first = await _sut.LocateAsync(Address(_street), TestContext.Current.CancellationToken);
        var second = await _sut.LocateAsync(Address($"  {_street.ToLowerInvariant()} "), TestContext.Current.CancellationToken);

        // Assert
        first.Should().Be(GeocodingResult.Found(-23.425269m, -51.938604m));
        second.Should().Be(first);
        _inner.Calls.Should().Be(1, "o mesmo endereço com outra caixa e espaços cai na mesma chave");
    }

    [Fact]
    public async Task LocateAsync_Should_StoreFoundWithTheConfiguredLifetime_AndHideTheAddressInTheKey()
    {
        // Arrange
        _inner.Next = GeocodingResult.Found(-23.4m, -51.9m);
        var address = Address(_street);

        // Act
        await _sut.LocateAsync(address, TestContext.Current.CancellationToken);

        // Assert
        var key = CachedGeocoder.KeyFor(address);
        key.ToString().Should().StartWith("pethost:listing:geocoding:").And.NotContain("Flores");
        (await _redis.GetDatabase().StringGetAsync(key)).ToString().Should().Be("found|-23.4|-51.9");
        (await _redis.GetDatabase().KeyTimeToLiveAsync(key)).Should().BeCloseTo(TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task LocateAsync_Should_CacheNotFoundForAShortTime()
    {
        // Arrange
        _inner.Next = GeocodingResult.NotFound;
        var address = Address(_street + " inexistente");

        // Act
        await _sut.LocateAsync(address, TestContext.Current.CancellationToken);
        var again = await _sut.LocateAsync(address, TestContext.Current.CancellationToken);

        // Assert
        again.Should().Be(GeocodingResult.NotFound);
        _inner.Calls.Should().Be(1);
        (await _redis.GetDatabase().KeyTimeToLiveAsync(CachedGeocoder.KeyFor(address))).Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task LocateAsync_Should_NotCache_When_TheMapIsUnavailable()
    {
        // Arrange
        _inner.Next = GeocodingResult.Unavailable;
        var address = Address(_street);

        // Act
        await _sut.LocateAsync(address, TestContext.Current.CancellationToken);
        await _sut.LocateAsync(address, TestContext.Current.CancellationToken);

        // Assert
        _inner.Calls.Should().Be(2, "falha do mapa não fica guardada");
        (await _redis.GetDatabase().KeyExistsAsync(CachedGeocoder.KeyFor(address))).Should().BeFalse();
    }

    [Fact]
    public async Task LocateAsync_Should_AskTheMapAgain_When_TheCachedValueIsUnreadable()
    {
        // Arrange
        var address = Address(_street);
        await _redis.GetDatabase().StringSetAsync(CachedGeocoder.KeyFor(address), "lixo");
        _inner.Next = GeocodingResult.Found(-23.4m, -51.9m);

        // Act
        var result = await _sut.LocateAsync(address, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(GeocodingResult.Found(-23.4m, -51.9m));
        _inner.Calls.Should().Be(1);
    }

    private static ListingAddressData Address(string street) =>
        new("87020-000", street, "120", null, "Zona 7", "Maringá", "PR");

    /// <summary>Mapa de mentira que conta quantas vezes foi chamado.</summary>
    private sealed class CountingGeocoder : IGeocoder
    {
        public GeocodingResult Next { get; set; } = GeocodingResult.NotFound;

        public int Calls { get; private set; }

        public Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }
}
