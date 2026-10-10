using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Pet"/> na tabela <c>pet.pets</c>. <c>owner_id</c> e <c>host_id</c> são
/// <b>FKs lógicas</b> para <c>owner.owners.id</c> e <c>host.hosts.id</c>, sem constraint
/// física: cada módulo tem o seu schema e não há restrição nem JOIN entre schemas (§5, §11).
/// </summary>
/// <remarks>
/// Os CHECKs repetem no banco as regras do domínio: exatamente um dono; porte sempre no
/// cachorro, opcional no gato, nunca nos outros; descrição só (e sempre) em exótico; enums só com os valores conhecidos.
/// </remarks>
internal sealed class PetConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        var dog = PetSpeciesValues.ToWire(PetSpecies.Dog);
        var cat = PetSpeciesValues.ToWire(PetSpecies.Cat);
        var exotic = PetSpeciesValues.ToWire(PetSpecies.Exotic);

        builder.ToTable("pets", table =>
        {
            table.HasCheckConstraint("ck_pets_one_keeper", "num_nonnulls(owner_id, host_id) = 1");
            table.HasCheckConstraint("ck_pets_species", $"species IN ({InList(PetSpeciesValues.All)})");
            table.HasCheckConstraint("ck_pets_size", $"size IS NULL OR size IN ({InList(PetSizeValues.All)})");
            table.HasCheckConstraint("ck_pets_sex", $"sex IN ({InList(PetSexValues.All)})");
            table.HasCheckConstraint("ck_pets_size_required_for_dogs", $"species <> '{dog}' OR size IS NOT NULL");
            table.HasCheckConstraint("ck_pets_size_only_for_dogs_and_cats", $"size IS NULL OR species IN ('{dog}', '{cat}')");
            table.HasCheckConstraint(
                "ck_pets_description_only_for_exotic",
                $"(species = '{exotic}') = (species_description IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion<PetIdConverter>()
            .ValueGeneratedNever();

        builder.Property(p => p.OwnerId)
            .HasColumnName("owner_id");

        builder.Property(p => p.HostId)
            .HasColumnName("host_id");

        // A ficha como tipo complexo: colunas na própria pets, sem tabela nem join.
        builder.ComplexProperty(p => p.Profile, profile =>
        {
            profile.Property(f => f.Species)
                .HasColumnName("species")
                .HasConversion<PetSpeciesConverter>()
                .HasMaxLength(20)
                .IsRequired();

            profile.Property(f => f.SpeciesDescription)
                .HasColumnName("species_description")
                .HasMaxLength(PetProfile.SpeciesDescriptionMaxLength);

            profile.Property(f => f.Name)
                .HasColumnName("name")
                .HasMaxLength(PetProfile.NameMaxLength)
                .IsRequired();

            profile.Property(f => f.PhotoUrl)
                .HasColumnName("photo_url")
                .HasMaxLength(PetProfile.PhotoUrlMaxLength);

            profile.Property(f => f.Breed)
                .HasColumnName("breed")
                .HasMaxLength(PetProfile.BreedMaxLength);

            profile.Property(f => f.Size)
                .HasColumnName("size")
                .HasConversion<PetSizeConverter>()
                .HasMaxLength(10);

            profile.Property(f => f.BirthDate)
                .HasColumnName("birth_date")
                .HasColumnType("date");

            profile.Property(f => f.Sex)
                .HasColumnName("sex")
                .HasConversion<PetSexConverter>()
                .HasMaxLength(10)
                .IsRequired();

            profile.Property(f => f.IsNeutered).HasColumnName("is_neutered").IsRequired();
            profile.Property(f => f.IsVaccinated).HasColumnName("is_vaccinated").IsRequired();

            profile.Property(f => f.MedicationNotes)
                .HasColumnName("medication_notes")
                .HasColumnType("text");

            profile.Property(f => f.FeedingNotes)
                .HasColumnName("feeding_notes")
                .HasColumnType("text");

            profile.Property(f => f.GoodWithDogs).HasColumnName("good_with_dogs").IsRequired();
            profile.Property(f => f.GoodWithCats).HasColumnName("good_with_cats").IsRequired();
            profile.Property(f => f.GoodWithKids).HasColumnName("good_with_kids").IsRequired();

            profile.Property(f => f.VetContact)
                .HasColumnName("vet_contact")
                .HasMaxLength(PetProfile.VetContactMaxLength);

            profile.Property(f => f.Notes)
                .HasColumnName("notes")
                .HasColumnType("text");

            // numeric(6,3): até 999,999 kg com precisão de grama (hamster, passarinho).
            profile.Property(f => f.WeightKg)
                .HasColumnName("weight_kg")
                .HasPrecision(6, PetProfile.WeightDecimals);

            profile.Property(f => f.Microchip)
                .HasColumnName("microchip")
                .HasColumnType($"char({PetProfile.MicrochipLength})");

            profile.Property(f => f.Allergies)
                .HasColumnName("allergies")
                .HasColumnType("text");
        });

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.DeactivatedAt)
            .HasColumnName("deactivated_at")
            .HasColumnType("timestamptz");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        // As duas listas: "meus pets" e "pets da casa do anfitrião".
        builder.HasIndex(p => p.OwnerId).HasDatabaseName("ix_pets_owner_id");
        builder.HasIndex(p => p.HostId).HasDatabaseName("ix_pets_host_id");

        // Microchip único entre os pets ativos: índice parcial uq_pets_microchip_active, criado
        // em SQL na migration AddUniqueMicrochipToPets (o EF não indexa coluna de tipo complexo).

        builder.Ignore(p => p.Keeper);
        builder.Ignore(p => p.DomainEvents);
    }

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
