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
            .HasConversion<FullNameConverter>()
            .HasMaxLength(FullName.MaxLength)
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
            .HasConversion<PhoneNumberConverter>()
            .HasMaxLength(PhoneNumber.MaxLength);

        builder.Property(u => u.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasConversion<AvatarUrlConverter>()
            .HasMaxLength(AvatarUrl.MaxLength);

        builder.Property(u => u.BirthDate)
            .HasColumnName("birth_date")
            .HasColumnType("date");

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.DeactivatedAt)
            .HasColumnName("deactivated_at")
            .HasColumnType("timestamptz");

        builder.Property(u => u.SuspendedAt)
            .HasColumnName("suspended_at")
            .HasColumnType("timestamptz");

        builder.Property(u => u.SuspensionReason)
            .HasColumnName("suspension_reason")
            .HasMaxLength(User.SuspensionReasonMaxLength);

        // Sem FK: o admin é um usuário desta mesma tabela, mas apagar a conta dele não
        // pode apagar nem bloquear o histórico de quem ele suspendeu.
        builder.Property(u => u.SuspendedBy)
            .HasColumnName("suspended_by");

        // Endereço como tipo complexo: colunas na própria users, sem tabela nem join.
        // Opcional porque o admin não tem endereço; tutor e anfitrião sempre têm.
        builder.ComplexProperty(u => u.Address, address =>
        {
            address.Property(a => a.ZipCode)
                .HasColumnName("zip_code")
                .HasConversion<ZipCodeConverter>()
                .HasColumnType($"char({ZipCode.Length})");

            address.Property(a => a.Street)
                .HasColumnName("street")
                .HasMaxLength(Address.StreetMaxLength);

            address.Property(a => a.Number)
                .HasColumnName("street_number")
                .HasMaxLength(Address.NumberMaxLength);

            address.Property(a => a.Complement)
                .HasColumnName("complement")
                .HasMaxLength(Address.ComplementMaxLength);

            address.Property(a => a.Neighborhood)
                .HasColumnName("neighborhood")
                .HasMaxLength(Address.NeighborhoodMaxLength);

            address.Property(a => a.City)
                .HasColumnName("city")
                .HasMaxLength(Address.CityMaxLength);

            address.Property(a => a.State)
                .HasColumnName("state")
                .HasConversion<StateCodeConverter>()
                .HasColumnType($"char({StateCode.Length})");
        });

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
