using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Birds;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;
using CriatorioVirtual.Infrastructure.Species;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Persistence;

public sealed class CriatorioVirtualDbContext(DbContextOptions<CriatorioVirtualDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string DefaultSchema = "app";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<BreedingFarm> BreedingFarms => Set<BreedingFarm>();

    public DbSet<BreedingFarmUser> BreedingFarmUsers => Set<BreedingFarmUser>();

    public DbSet<Bird> Birds => Set<Bird>();

    public DbSet<GenealogyNode> GenealogyNodes => Set<GenealogyNode>();

    public DbSet<SpeciesEntity> Species => Set<SpeciesEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("users", "identity");
            user.Property(candidate => candidate.SelectedBreedingFarmId)
                .HasColumnName("SelectedBreedingFarmId");
            user.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.SelectedBreedingFarmId)
                .OnDelete(DeleteBehavior.SetNull);
            user.HasIndex(applicationUser => applicationUser.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("EmailIndex")
                .HasFilter("\"NormalizedEmail\" IS NOT NULL");
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles", "identity");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity");
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys", "identity");

        modelBuilder.Entity<BreedingFarm>(farm =>
        {
            farm.ToTable("breeding_farms", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_breeding_farms_name_not_blank",
                    "btrim(\"Name\") <> ''");
                table.HasCheckConstraint(
                    "ck_breeding_farms_official_registration_not_blank",
                    "\"OfficialRegistrationNumber\" IS NULL OR btrim(\"OfficialRegistrationNumber\") <> ''");
            });
            farm.HasKey(candidate => candidate.Id);
            farm.Property(candidate => candidate.Name).HasMaxLength(200).IsRequired();
            farm.Property(candidate => candidate.ResponsibleName).HasMaxLength(200).IsRequired();
            farm.Property(candidate => candidate.ContactEmail).HasMaxLength(320).IsRequired();
            farm.Property(candidate => candidate.ContactPhone).HasMaxLength(32);
            farm.Property(candidate => candidate.OfficialRegistrationNumber).HasMaxLength(100);
            farm.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            farm.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            farm.Property<uint>("xmin").IsRowVersion();
            farm.HasIndex(candidate => candidate.OfficialRegistrationNumber)
                .IsUnique()
                .HasDatabaseName("ux_breeding_farms_official_registration_number")
                .HasFilter("\"OfficialRegistrationNumber\" IS NOT NULL");

            farm.OwnsOne(candidate => candidate.Address, address =>
            {
                address.Property(candidate => candidate.Street)
                    .HasColumnName("AddressStreet")
                    .HasMaxLength(200);
                address.Property(candidate => candidate.Number)
                    .HasColumnName("AddressNumber")
                    .HasMaxLength(32);
                address.Property(candidate => candidate.Complement)
                    .HasColumnName("AddressComplement")
                    .HasMaxLength(100);
                address.Property(candidate => candidate.Neighborhood)
                    .HasColumnName("AddressNeighborhood")
                    .HasMaxLength(120);
                address.Property(candidate => candidate.City)
                    .HasColumnName("AddressCity")
                    .HasMaxLength(120);
                address.Property(candidate => candidate.State)
                    .HasColumnName("AddressState")
                    .HasMaxLength(100);
                address.Property(candidate => candidate.PostalCode)
                    .HasColumnName("AddressPostalCode")
                    .HasMaxLength(20);
            });
        });

        modelBuilder.Entity<BreedingFarmUser>(membership =>
        {
            membership.ToTable("breeding_farm_users", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_breeding_farm_users_role_valid",
                    "\"Role\" IN (1, 2, 3, 4)");
            });
            membership.HasKey(candidate => new { candidate.BreedingFarmId, candidate.UserId });
            membership.Property(candidate => candidate.Role)
                .HasConversion<int>()
                .IsRequired();
            membership.Property(candidate => candidate.IsActive).IsRequired();
            membership.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            membership.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Cascade);
            membership.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(candidate => candidate.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            membership.HasIndex(candidate => candidate.BreedingFarmId)
                .IsUnique()
                .HasDatabaseName("ux_breeding_farm_users_active_owner")
                .HasFilter("\"IsActive\" = TRUE AND \"Role\" = 1");
        });

        modelBuilder.Entity<Bird>(bird =>
        {
            bird.ToTable("birds", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_birds_name_not_blank",
                    "btrim(\"Name\") <> ''");
                table.HasCheckConstraint(
                    "ck_birds_sex_valid",
                    "\"Sex\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_birds_status_valid",
                    "\"Status\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_birds_ring_number_format",
                    "\"RingNumber\" IS NULL OR \"RingNumber\" ~ '^[0-9]{6}$'");
                table.HasCheckConstraint(
                    "ck_birds_father_source_exclusive",
                    "\"FatherBirdId\" IS NULL OR \"ExternalFatherName\" IS NULL");
                table.HasCheckConstraint(
                    "ck_birds_mother_source_exclusive",
                    "\"MotherBirdId\" IS NULL OR \"ExternalMotherName\" IS NULL");
                table.HasCheckConstraint(
                    "ck_birds_external_father_sex",
                    "\"ExternalFatherSex\" IS NULL OR (\"ExternalFatherName\" IS NOT NULL AND \"ExternalFatherSex\" = 1)");
                table.HasCheckConstraint(
                    "ck_birds_external_mother_sex",
                    "\"ExternalMotherSex\" IS NULL OR (\"ExternalMotherName\" IS NOT NULL AND \"ExternalMotherSex\" = 2)");
                table.HasCheckConstraint(
                    "ck_birds_death_date_after_birth_date",
                    "\"DeathDate\" IS NULL OR \"BirthDate\" IS NULL OR \"DeathDate\" >= \"BirthDate\"");
            });
            bird.HasKey(candidate => candidate.Id);
            bird.HasAlternateKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .HasName("ak_birds_farm_id");
            bird.Property(candidate => candidate.BreedingFarmId).IsRequired();
            bird.Property(candidate => candidate.Name).HasMaxLength(100).IsRequired();
            bird.Property(candidate => candidate.SpeciesId).IsRequired();
            bird.Property(candidate => candidate.Sex).HasConversion<int>().IsRequired();
            bird.Property(candidate => candidate.BirthDate).HasColumnType("date");
            bird.Property(candidate => candidate.DeathDate).HasColumnType("date");
            bird.Property(candidate => candidate.RingNumber).HasMaxLength(6);
            bird.Property(candidate => candidate.ExternalFatherName).HasMaxLength(200);
            bird.Property(candidate => candidate.ExternalFatherSex).HasConversion<int>();
            bird.Property(candidate => candidate.ExternalMotherName).HasMaxLength(200);
            bird.Property(candidate => candidate.ExternalMotherSex).HasConversion<int>();
            bird.Property(candidate => candidate.Notes).HasMaxLength(2000);
            bird.Property(candidate => candidate.Status).HasConversion<int>().IsRequired();
            bird.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            bird.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            bird.HasIndex(candidate => candidate.RingNumber)
                .IsUnique()
                .HasDatabaseName("ux_birds_ring_number")
                .HasFilter("\"RingNumber\" IS NOT NULL");
            bird.HasIndex(candidate => new { candidate.BreedingFarmId, candidate.Status })
                .HasDatabaseName("ix_birds_farm_status");
            bird.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Cascade);
            bird.HasOne<SpeciesEntity>()
                .WithMany()
                .HasForeignKey(candidate => candidate.SpeciesId)
                .OnDelete(DeleteBehavior.Restrict);
            bird.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.FatherBirdId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Restrict);
            bird.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.MotherBirdId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GenealogyNode>(node =>
        {
            node.ToTable("genealogy_nodes", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_genealogy_nodes_root_position",
                    "(\"IsRoot\" = TRUE AND \"Position\" = 'root' AND \"LinkedBirdId\" = \"BirdId\") OR (\"IsRoot\" = FALSE AND \"BreedingFarmId\" IS NOT NULL AND \"Position\" <> 'root' AND \"LinkedBirdId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_genealogy_nodes_snapshot_required",
                    "\"IsRoot\" = TRUE OR (\"SnapshotName\" IS NOT NULL AND \"SnapshotSex\" IS NOT NULL AND \"SnapshotStatus\" IS NOT NULL)");
            });
            node.HasKey(candidate => candidate.Id);
            node.Property(candidate => candidate.BreedingFarmId).IsRequired();
            node.Property(candidate => candidate.BirdId).IsRequired();
            node.Property(candidate => candidate.GenealogyRootId).IsRequired();
            node.Property(candidate => candidate.Position).HasMaxLength(100).IsRequired();
            node.Property(candidate => candidate.LinkedBirdId);
            node.Property(candidate => candidate.SnapshotName).HasMaxLength(100);
            node.Property(candidate => candidate.SnapshotSex).HasConversion<int>();
            node.Property(candidate => candidate.SnapshotBirthDate).HasColumnType("date");
            node.Property(candidate => candidate.SnapshotRingNumber).HasMaxLength(6);
            node.Property(candidate => candidate.SnapshotStatus).HasConversion<int>();
            node.Property(candidate => candidate.IsRoot).IsRequired();
            node.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            node.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            node.HasIndex(candidate => candidate.BirdId)
                .IsUnique()
                .HasDatabaseName("ux_genealogy_nodes_bird_root")
                .HasFilter("\"IsRoot\" = TRUE");
            node.HasIndex(candidate => new { candidate.GenealogyRootId, candidate.Position })
                .IsUnique()
                .HasDatabaseName("ux_genealogy_nodes_root_position");
            node.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.BirdId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Cascade);
            node.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.LinkedBirdId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Restrict);
            node.HasOne<GenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => candidate.GenealogyRootId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SpeciesEntity>(species =>
        {
            species.ToTable("species", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_species_scientific_name_not_blank",
                    "btrim(\"ScientificName\") <> ''");
                table.HasCheckConstraint(
                    "ck_species_popular_name_not_blank",
                    "btrim(\"PopularName\") <> ''");
            });
            species.HasKey(candidate => candidate.Id);
            species.Property(candidate => candidate.ScientificName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.PopularName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.NormalizedScientificName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.NormalizedPopularName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.IsActive).IsRequired();
            species.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            species.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            species.HasIndex(candidate => new
            {
                candidate.NormalizedPopularName,
                candidate.NormalizedScientificName
            })
                .IsUnique()
                .HasDatabaseName("ux_species_normalized_names");
            species.HasData(SpeciesCatalogSeed.All);
        });
    }
}
