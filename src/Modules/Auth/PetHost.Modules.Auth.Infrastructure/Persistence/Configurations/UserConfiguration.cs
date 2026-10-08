using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="User"/> na tabela <c>auth.users</c>. Tamanhos e nulidade
/// vêm do dicionário de dados, tabela <c>users</c>.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_role",
            $"role IN ('{UserRoleValues.Owner}', '{UserRoleValues.Host}', '{UserRoleValues.Admin}')"));

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion<UserIdConverter>()
            .ValueGeneratedNever();

        builder.Property(u => u.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(User.FullNameMaxLength)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasConversion<EmailConverter>()
            .HasMaxLength(Email.MaxLength)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasConversion<PasswordHashConverter>()
            .HasMaxLength(PasswordHash.MaxLength)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion<UserRoleConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(u => u.Phone)
            .HasColumnName("phone")
            .HasMaxLength(User.PhoneMaxLength);

        builder.Property(u => u.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(User.AvatarUrlMaxLength);

        builder.Property(u => u.Neighborhood)
            .HasColumnName("neighborhood")
            .HasMaxLength(User.NeighborhoodMaxLength);

        builder.Property(u => u.City)
            .HasColumnName("city")
            .HasMaxLength(User.CityMaxLength);

        builder.Property(u => u.State)
            .HasColumnName("state")
            .HasColumnType($"char({User.StateLength})");

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // UK¹ do dicionário: o mesmo e-mail pode existir uma vez por papel.
        // É este índice que permite a Dona Cida ter conta de tutora e de anfitriã.
        builder.HasIndex(u => new { u.Email, u.Role })
            .HasDatabaseName("uq_users_email_role")
            .IsUnique();

        // Domain events vivem só em memória, nunca em coluna.
        builder.Ignore(u => u.DomainEvents);
    }
}
