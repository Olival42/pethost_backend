using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Review"/> na tabela <c>review.reviews</c> e as notas dos pets em
/// <c>review.review_pet_ratings</c>. <c>booking_id</c>, <c>owner_id</c>, <c>host_id</c>,
/// <c>listing_id</c> e <c>pet_id</c> são <b>FKs lógicas</b>, sem constraint física: cada módulo tem
/// o seu schema e não há restrição nem JOIN entre schemas (§5, §11).
/// </summary>
/// <remarks>
/// Uma avaliação por lado por reserva: índice único em (<c>booking_id</c>, <c>author_side</c>), que
/// vale também para a apagada (soft delete). Os CHECKs repetem no banco as regras do domínio (nota
/// de 1 a 5, lado, resposta e ocultação completas). Concorrência otimista pelo <c>xmin</c>: duas
/// edições ao mesmo tempo dão <c>409 REVIEW_CONFLICT</c>.
/// </remarks>
internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", table =>
        {
            table.HasCheckConstraint("ck_reviews_rating", $"rating BETWEEN {Review.MinRating} AND {Review.MaxRating}");
            table.HasCheckConstraint(
                "ck_reviews_author_side",
                $"author_side IN ({string.Join(", ", ReviewPartyValues.All.Select(v => $"'{v}'"))})");
            table.HasCheckConstraint("ck_reviews_reply", "(reply_text IS NULL) = (replied_at IS NULL)");
            table.HasCheckConstraint("ck_reviews_hidden", "(hidden_at IS NULL) = (hidden_by IS NULL) AND (hidden_at IS NULL) = (hidden_reason IS NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion<ReviewIdConverter>()
            .ValueGeneratedNever();

        builder.Property(r => r.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(r => r.AuthorSide)
            .HasColumnName("author_side")
            .HasConversion<ReviewPartyConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(r => r.HostId).HasColumnName("host_id").IsRequired();
        builder.Property(r => r.ListingId).HasColumnName("listing_id").IsRequired();

        builder.Property(r => r.Rating).HasColumnName("rating").HasColumnType("smallint").IsRequired();
        builder.Property(r => r.Comment).HasColumnName("comment").HasMaxLength(Review.CommentMaxLength);

        builder.Property(r => r.ReplyText).HasColumnName("reply_text").HasMaxLength(Review.CommentMaxLength);
        builder.Property(r => r.RepliedAt).HasColumnName("replied_at").HasColumnType("timestamptz");

        builder.Property(r => r.StayEndedAt).HasColumnName("stay_ended_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

        builder.Property(r => r.PublishedAt).HasColumnName("published_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.SubjectNotifiedAt).HasColumnName("subject_notified_at").HasColumnType("timestamptz");

        builder.Property(r => r.HiddenAt).HasColumnName("hidden_at").HasColumnType("timestamptz");
        builder.Property(r => r.HiddenBy).HasColumnName("hidden_by");
        builder.Property(r => r.HiddenReason).HasColumnName("hidden_reason").HasMaxLength(Review.CommentMaxLength);

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(r => r.PetRatings, pets =>
        {
            pets.ToTable("review_pet_ratings", table =>
                table.HasCheckConstraint("ck_review_pet_ratings_rating", $"rating BETWEEN {Review.MinRating} AND {Review.MaxRating}"));

            pets.WithOwner().HasForeignKey("ReviewId");
            pets.Property<ReviewId>("ReviewId").HasColumnName("review_id").HasConversion<ReviewIdConverter>();
            pets.Property(p => p.PetId).HasColumnName("pet_id");
            pets.HasKey("ReviewId", nameof(ReviewPetRating.PetId));

            pets.Property(p => p.Rating).HasColumnName("rating").HasColumnType("smallint").IsRequired();
            pets.Property(p => p.Comment).HasColumnName("comment").HasMaxLength(Review.CommentMaxLength);

            pets.HasIndex(p => p.PetId).HasDatabaseName("ix_review_pet_ratings_pet_id");
        });

        builder.Navigation(r => r.PetRatings).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => new { r.BookingId, r.AuthorSide })
            .IsUnique()
            .HasDatabaseName("uq_reviews_booking_id_author_side");

        // Listas (do anfitrião, do cantinho, do tutor, as minhas) e médias, da mais nova para a mais
        // antiga, só das visíveis.
        builder.HasIndex(r => new { r.HostId, r.AuthorSide, r.CreatedAt })
            .HasDatabaseName("ix_reviews_host_id_author_side_created_at")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(r => new { r.ListingId, r.AuthorSide, r.CreatedAt })
            .HasDatabaseName("ix_reviews_listing_id_author_side_created_at")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(r => new { r.OwnerId, r.AuthorSide, r.CreatedAt })
            .HasDatabaseName("ix_reviews_owner_id_author_side_created_at")
            .HasFilter("deleted_at IS NULL");

        // A tarefa que avisa o avaliado das avaliações que acabaram de ser publicadas.
        builder.HasIndex(r => r.PublishedAt)
            .HasDatabaseName("ix_reviews_published_at_to_notify")
            .HasFilter("subject_notified_at IS NULL AND deleted_at IS NULL");

        builder.Ignore(r => r.SubjectSide);
        builder.Ignore(r => r.IsDeleted);
        builder.Ignore(r => r.IsHidden);
        builder.Ignore(r => r.EditableUntil);
        builder.Ignore(r => r.DomainEvents);
    }
}
