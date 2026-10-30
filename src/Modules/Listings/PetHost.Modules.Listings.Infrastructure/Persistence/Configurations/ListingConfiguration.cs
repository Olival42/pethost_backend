using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Listing"/> na tabela <c>listing.listings</c>. <c>host_id</c> é <b>FK
/// lógica</b> para <c>host.hosts.id</c>, sem constraint física: cada módulo tem o seu schema e
/// não há restrição nem JOIN entre schemas (§5, §11). Também não é único: empresa tem um
/// cantinho por filial; o limite da pessoa física fica na aplicação, que sabe o tipo do anfitrião.
/// </summary>
/// <remarks>
/// Tipos aceitos e dias da semana são conjuntos de bits (<c>accepted_species</c>,
/// <c>available_weekdays</c>). Os CHECKs repetem no banco as faixas do domínio. A busca por raio
/// usa o ponto aproximado (<c>approx_latitude</c>, <c>approx_longitude</c>), nunca o exato; o
/// índice dela, só dos ativos, é criado em SQL na migration (o EF não indexa coluna de tipo complexo).
/// </remarks>
internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("listings", table =>
        {
            table.HasCheckConstraint("ck_listings_home_type", $"home_type IS NULL OR home_type IN ({InList(HomeTypeValues.All)})");
            table.HasCheckConstraint("ck_listings_capacity", $"capacity BETWEEN 1 AND {ListingDetails.MaxCapacityCompany}");
            table.HasCheckConstraint(
                "ck_listings_nightly_rate",
                string.Create(CultureInfo.InvariantCulture, $"nightly_rate BETWEEN {ListingDetails.MinNightlyRate} AND {ListingDetails.MaxNightlyRate}"));
            table.HasCheckConstraint("ck_listings_accepted_species", $"accepted_species BETWEEN 1 AND {(int)AcceptedSpeciesValues.Every}");
            table.HasCheckConstraint("ck_listings_available_weekdays", $"available_weekdays BETWEEN 1 AND {(int)WeekdaysValues.Every}");
            table.HasCheckConstraint("ck_listings_latitude", "latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_listings_longitude", "longitude BETWEEN -180 AND 180");
            table.HasCheckConstraint("ck_listings_approx_latitude", "approx_latitude BETWEEN -90 AND 90");
            table.HasCheckConstraint("ck_listings_approx_longitude", "approx_longitude BETWEEN -180 AND 180");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion<ListingIdConverter>()
            .ValueGeneratedNever();

        builder.Property(l => l.HostId)
            .HasColumnName("host_id")
            .IsRequired();

        builder.ComplexProperty(l => l.Details, details =>
        {
            details.Property(d => d.Title).HasColumnName("title").HasMaxLength(ListingDetails.TitleMaxLength).IsRequired();
            details.Property(d => d.Description).HasColumnName("description").HasColumnType("text").IsRequired();

            details.Property(d => d.HomeType)
                .HasColumnName("home_type")
                .HasConversion<HomeTypeConverter>()
                .HasMaxLength(20);

            details.Property(d => d.HasYard).HasColumnName("has_yard").IsRequired();
            details.Property(d => d.HasDogs).HasColumnName("has_dogs").IsRequired();
            details.Property(d => d.HasCats).HasColumnName("has_cats").IsRequired();
            details.Property(d => d.HasKids).HasColumnName("has_kids").IsRequired();

            details.Property(d => d.AcceptedSpecies)
                .HasColumnName("accepted_species")
                .HasConversion<int>()
                .IsRequired();

            details.Property(d => d.AcceptsSmall).HasColumnName("accepts_small").IsRequired();
            details.Property(d => d.AcceptsMedium).HasColumnName("accepts_medium").IsRequired();
            details.Property(d => d.AcceptsLarge).HasColumnName("accepts_large").IsRequired();
            details.Property(d => d.Capacity).HasColumnName("capacity").IsRequired();

            // numeric(10,2): até R$ 99.999.999,99 com centavos; nunca float (§11).
            details.Property(d => d.NightlyRate)
                .HasColumnName("nightly_rate")
                .HasPrecision(10, ListingDetails.NightlyRateDecimals)
                .IsRequired();

            details.Property(d => d.HouseRules).HasColumnName("house_rules").HasColumnType("text");

            details.Property(d => d.AvailableWeekdays)
                .HasColumnName("available_weekdays")
                .HasConversion<short>()
                .HasColumnType("smallint")
                .IsRequired();
        });

        builder.ComplexProperty(l => l.Address, address =>
        {
            address.Property(a => a.ZipCode).HasColumnName("zip_code").HasColumnType($"char({ListingAddress.ZipCodeLength})").IsRequired();
            address.Property(a => a.Street).HasColumnName("street").HasMaxLength(ListingAddress.StreetMaxLength).IsRequired();
            address.Property(a => a.Number).HasColumnName("street_number").HasMaxLength(ListingAddress.NumberMaxLength).IsRequired();
            address.Property(a => a.Complement).HasColumnName("complement").HasMaxLength(ListingAddress.ComplementMaxLength);
            address.Property(a => a.Neighborhood).HasColumnName("neighborhood").HasMaxLength(ListingAddress.NeighborhoodMaxLength).IsRequired();
            address.Property(a => a.City).HasColumnName("city").HasMaxLength(ListingAddress.CityMaxLength).IsRequired();
            address.Property(a => a.State).HasColumnName("state").HasColumnType($"char({ListingAddress.StateLength})").IsRequired();
        });

        // numeric(9,6): graus com 6 casas (~10 cm).
        builder.ComplexProperty(l => l.Location, location =>
        {
            location.Property(p => p.Latitude).HasColumnName("latitude").HasPrecision(9, GeoPoint.Decimals).IsRequired();
            location.Property(p => p.Longitude).HasColumnName("longitude").HasPrecision(9, GeoPoint.Decimals).IsRequired();
        });

        // numeric(6,3): o ponto do mapa público, com 3 casas (~100 m).
        builder.ComplexProperty(l => l.ApproximateLocation, location =>
        {
            location.Property(p => p.Latitude).HasColumnName("approx_latitude").HasPrecision(6, GeoPoint.ApproximateDecimals).IsRequired();
            location.Property(p => p.Longitude).HasColumnName("approx_longitude").HasPrecision(6, GeoPoint.ApproximateDecimals).IsRequired();
        });

        builder.Property(l => l.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(l => l.DeactivatedAt)
            .HasColumnName("deactivated_at")
            .HasColumnType("timestamptz");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // "Meus cantinhos" e a contagem do limite da pessoa física.
        builder.HasIndex(l => l.HostId).HasDatabaseName("ix_listings_host_id");

        // Fotos: a coleção é o campo _photos (a propriedade Photos é uma cópia ordenada).
        builder.HasMany<ListingPhoto>(ListingsDbContext.PhotosField)
            .WithOne()
            .HasForeignKey(photo => photo.ListingId)
            .HasConstraintName("fk_listing_photos_listings")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(l => l.Photos);
        builder.Ignore(l => l.CanAddPhoto);
        builder.Ignore(l => l.DomainEvents);
    }

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
