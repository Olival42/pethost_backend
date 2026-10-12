using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="ListingPhoto"/> na tabela <c>listing.listing_photos</c>. A FK para
/// <c>listing.listings</c> é física: as duas tabelas são do mesmo módulo (§11 só proíbe entre módulos).
/// </summary>
/// <remarks>
/// O banco repete o limite: a posição vai de 1 a <see cref="Listing.MaxPhotos"/> e é única por
/// cantinho. Essa unicidade é uma constraint <b>deferrable</b> (<c>uq_listing_photos_listing_id_position</c>),
/// criada em SQL na migration: reordenar troca posições entre fotos na mesma transação, e a
/// conferência fica para o commit. O hash da imagem é único por cantinho.
/// </remarks>
internal sealed class ListingPhotoConfiguration : IEntityTypeConfiguration<ListingPhoto>
{
    public void Configure(EntityTypeBuilder<ListingPhoto> builder)
    {
        builder.ToTable("listing_photos", table =>
            table.HasCheckConstraint("ck_listing_photos_position", $"position BETWEEN 1 AND {Listing.MaxPhotos}"));

        builder.HasKey(photo => photo.Id);

        builder.Property(photo => photo.Id)
            .HasColumnName("id")
            .HasConversion<ListingPhotoIdConverter>()
            .ValueGeneratedNever();

        builder.Property(photo => photo.ListingId)
            .HasColumnName("listing_id")
            .HasConversion<ListingIdConverter>()
            .IsRequired();

        builder.Property(photo => photo.Url)
            .HasColumnName("url")
            .HasMaxLength(ListingPhoto.UrlMaxLength)
            .IsRequired();

        builder.Property(photo => photo.ContentHash)
            .HasColumnName("content_hash")
            .HasColumnType("char(64)")
            .IsRequired();

        builder.Property(photo => photo.Position)
            .HasColumnName("position")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(photo => photo.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(photo => new { photo.ListingId, photo.ContentHash })
            .IsUnique()
            .HasDatabaseName("uq_listing_photos_listing_id_content_hash");
    }
}
