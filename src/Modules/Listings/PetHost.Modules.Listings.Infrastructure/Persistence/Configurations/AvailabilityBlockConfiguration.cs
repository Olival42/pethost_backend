using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="AvailabilityBlock"/> na tabela <c>listing.availability_blocks</c>. A FK para
/// <c>listing.listings</c> é física (mesmo módulo) e apaga junto.
/// </summary>
/// <remarks>
/// O banco repete a regra das datas (<c>end_date &gt;= start_date</c>). O índice por cantinho e
/// datas atende ao pedido de reserva, à leitura da agenda e à busca por datas.
/// </remarks>
internal sealed class AvailabilityBlockConfiguration : IEntityTypeConfiguration<AvailabilityBlock>
{
    public void Configure(EntityTypeBuilder<AvailabilityBlock> builder)
    {
        builder.ToTable("availability_blocks", table =>
            table.HasCheckConstraint("ck_availability_blocks_period", "end_date >= start_date"));

        builder.HasKey(block => block.Id);

        builder.Property(block => block.Id)
            .HasColumnName("id")
            .HasConversion<AvailabilityBlockIdConverter>()
            .ValueGeneratedNever();

        builder.Property(block => block.ListingId)
            .HasColumnName("listing_id")
            .HasConversion<ListingIdConverter>()
            .IsRequired();

        builder.Property(block => block.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(block => block.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(block => block.Reason)
            .HasColumnName("reason")
            .HasMaxLength(AvailabilityBlock.ReasonMaxLength);

        builder.Property(block => block.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Ignore(block => block.Days);
        builder.Ignore(block => block.DomainEvents);

        builder.HasOne<Listing>()
            .WithMany()
            .HasForeignKey(block => block.ListingId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_availability_blocks_listings_listing_id");

        builder.HasIndex(block => new { block.ListingId, block.StartDate, block.EndDate })
            .HasDatabaseName("ix_availability_blocks_listing_id_start_date_end_date");
    }
}
