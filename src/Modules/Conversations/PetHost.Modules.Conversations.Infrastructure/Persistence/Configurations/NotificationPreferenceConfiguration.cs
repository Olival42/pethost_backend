using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Conversations.Domain.Preferences;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="NotificationPreference"/> na tabela <c>conversation.notification_preferences</c>.
/// <c>user_id</c> é a chave e FK lógica para <c>auth.users.id</c>, sem constraint física (§5, §11).
/// Só existe linha para a conta que mudou a escolha; sem linha, recebe tudo.
/// </summary>
internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(p => p.UnreadMessagesEmail).HasColumnName("unread_messages_email").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.Ignore(p => p.DomainEvents);
    }
}
