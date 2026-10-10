using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="PetPhoto"/> na tabela <c>pet.pet_photos</c>. A FK para <c>pet.pets</c> é
/// física: as duas tabelas são do mesmo módulo e do mesmo schema (§11 só proíbe entre módulos).
/// </summary>
/// <remarks>
/// O banco repete o limite de fotos: a vaga (<c>position</c>) vai de 1 a <see cref="Pet.MaxPhotos"/>
/// e é única por pet. Dois uploads ao mesmo tempo não passam de 3 fotos. O hash da imagem também
/// é único por pet: a mesma imagem não entra duas vezes.
/// </remarks>
internal sealed class PetPhotoConfiguration : IEntityTypeConfiguration<PetPhoto>
{
    public void Configure(EntityTypeBuilder<PetPhoto> builder)
    {
        builder.ToTable("pet_photos", table =>
            table.HasCheckConstraint("ck_pet_photos_position", $"position BETWEEN 1 AND {Pet.MaxPhotos}"));

        builder.HasKey(photo => photo.Id);

        builder.Property(photo => photo.Id)
            .HasColumnName("id")
            .HasConversion<PetPhotoIdConverter>()
            .ValueGeneratedNever();

        builder.Property(photo => photo.PetId)
            .HasColumnName("pet_id")
            .HasConversion<PetIdConverter>()
            .IsRequired();

        builder.Property(photo => photo.Url)
            .HasColumnName("url")
            .HasMaxLength(PetPhoto.UrlMaxLength)
            .IsRequired();

        builder.Property(photo => photo.ContentHash)
            .HasColumnName("content_hash")
            .HasColumnType("char(64)");

        builder.Property(photo => photo.Position)
            .HasColumnName("position")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(photo => photo.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(photo => new { photo.PetId, photo.Position })
            .IsUnique()
            .HasDatabaseName("uq_pet_photos_pet_id_position");

        // A mesma imagem não entra duas vezes no mesmo pet. Fotos antigas, sem hash, ficam de fora.
        builder.HasIndex(photo => new { photo.PetId, photo.ContentHash })
            .IsUnique()
            .HasFilter("content_hash IS NOT NULL")
            .HasDatabaseName("uq_pet_photos_pet_id_content_hash");
    }
}
