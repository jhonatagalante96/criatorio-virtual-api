using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Domain.Documents;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;
using CriatorioVirtual.Infrastructure.Species;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Persistence;

public sealed class CriatorioVirtualDbContext(DbContextOptions<CriatorioVirtualDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        IdentityRole<Guid>,
        Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>,
        IdentityUserPasskey<Guid>>(options), IDataProtectionKeyContext
{
    public const string DefaultSchema = "app";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<BreedingFarm> BreedingFarms => Set<BreedingFarm>();

    public DbSet<BreedingFarmUser> BreedingFarmUsers => Set<BreedingFarmUser>();

    public DbSet<Bird> Birds => Set<Bird>();

    public DbSet<GenealogyNode> GenealogyNodes => Set<GenealogyNode>();

    public DbSet<SpeciesEntity> Species => Set<SpeciesEntity>();

    public DbSet<Reproduction> Reproductions => Set<Reproduction>();

    public DbSet<InternalTransferRequest> InternalTransferRequests => Set<InternalTransferRequest>();

    public DbSet<ExternalTransfer> ExternalTransfers => Set<ExternalTransfer>();

    public DbSet<BirdAttachment> BirdAttachments => Set<BirdAttachment>();

    public DbSet<BirdDocument> BirdDocuments => Set<BirdDocument>();

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
        modelBuilder.Entity<ApplicationUser>().Property(user => user.PhoneNumber).HasMaxLength(256);
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(login => login.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(login => login.ProviderKey).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(token => token.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(token => token.Name).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserPasskey<Guid>>(passkey =>
        {
            passkey.HasKey(candidate => candidate.CredentialId);
            passkey.ToTable("user_passkeys", "identity");
            passkey.Property(candidate => candidate.CredentialId).HasMaxLength(1024);
            passkey.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(candidate => candidate.UserId)
                .IsRequired();
            passkey.OwnsOne(candidate => candidate.Data).ToJson();
        });
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
            bird.Property(candidate => candidate.PrimaryPhotoId);
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
            bird.Property<uint>("xmin").IsRowVersion();
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
            bird.HasOne<BirdAttachment>()
                .WithMany()
                .HasForeignKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    BirdId = candidate.Id,
                    candidate.PrimaryPhotoId
                })
                .HasPrincipalKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.BirdId,
                    candidate.Id
                })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reproduction>(reproduction =>
        {
            reproduction.ToTable("reproductions", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_reproductions_distinct_birds",
                    "\"MaleBirdId\" <> \"FemaleBirdId\"");
                table.HasCheckConstraint(
                    "ck_reproductions_date_range",
                    "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                table.HasCheckConstraint(
                    "ck_reproductions_status_valid",
                    "\"Status\" IN (1, 2, 3)");
            });
            reproduction.HasKey(candidate => candidate.Id);
            reproduction.Property(candidate => candidate.BreedingFarmId).IsRequired();
            reproduction.Property(candidate => candidate.MaleBirdId).IsRequired();
            reproduction.Property(candidate => candidate.FemaleBirdId).IsRequired();
            reproduction.Property(candidate => candidate.StartDate)
                .HasColumnType("date")
                .IsRequired();
            reproduction.Property(candidate => candidate.EndDate)
                .HasColumnType("date");
            reproduction.Property(candidate => candidate.Notes).HasMaxLength(2000);
            reproduction.Property(candidate => candidate.Status)
                .HasConversion<int>()
                .IsRequired();
            reproduction.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            reproduction.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            reproduction.Property<uint>("xmin").IsRowVersion();
            reproduction.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.Status,
                candidate.StartDate
            }).HasDatabaseName("ix_reproductions_farm_status_start_date");
            reproduction.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.MaleBirdId,
                candidate.FemaleBirdId
            }).HasDatabaseName("ix_reproductions_farm_pair");
            reproduction.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Cascade);
            // Reproduction history remains owned by the farm where it was recorded even
            // when a referenced bird later moves to another farm. The bird identifier is
            // globally unique; authorization still scopes every query by BreedingFarmId.
            reproduction.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.MaleBirdId)
                .OnDelete(DeleteBehavior.Restrict);
            reproduction.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.FemaleBirdId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InternalTransferRequest>(transferRequest =>
        {
            transferRequest.ToTable("internal_transfer_requests", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_distinct_farms",
                    "\"SourceBreedingFarmId\" <> \"DestinationBreedingFarmId\"");
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_status_valid",
                    "\"Status\" IN (1, 2, 3, 4)");
            });
            transferRequest.HasKey(candidate => candidate.Id);
            transferRequest.Property(candidate => candidate.SourceBreedingFarmId).IsRequired();
            transferRequest.Property(candidate => candidate.DestinationBreedingFarmId).IsRequired();
            transferRequest.Property(candidate => candidate.BirdId).IsRequired();
            transferRequest.Property(candidate => candidate.RequestedByUserId).IsRequired();
            transferRequest.Property(candidate => candidate.Status)
                .HasConversion<int>()
                .IsRequired();
            transferRequest.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            transferRequest.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            transferRequest.HasIndex(candidate => candidate.BirdId)
                .IsUnique()
                .HasDatabaseName("ux_internal_transfer_requests_bird_pending")
                .HasFilter("\"Status\" = 1");
            transferRequest.HasIndex(candidate => new
            {
                candidate.SourceBreedingFarmId,
                candidate.Status,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_internal_transfer_requests_source_status_created_at");
            transferRequest.HasIndex(candidate => new
            {
                candidate.DestinationBreedingFarmId,
                candidate.Status,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_internal_transfer_requests_destination_status_created_at");
            transferRequest.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BirdId)
                .OnDelete(DeleteBehavior.Restrict);
            transferRequest.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.SourceBreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            transferRequest.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.DestinationBreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            transferRequest.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(candidate => candidate.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BirdAttachment>(attachment =>
        {
            attachment.ToTable("bird_attachments", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_bird_attachments_object_key_not_blank",
                    "btrim(\"ObjectKey\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_attachments_file_name_not_blank",
                    "btrim(\"FileName\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_attachments_content_type_not_blank",
                    "btrim(\"ContentType\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_attachments_length_positive",
                    "\"Length\" > 0");
                table.HasCheckConstraint(
                    "ck_bird_attachments_cleanup_requires_deletion",
                    "\"StorageCleanupPending\" = FALSE OR \"DeletedAtUtc\" IS NOT NULL");
            });
            attachment.HasKey(candidate => candidate.Id);
            attachment.HasAlternateKey(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.BirdId,
                candidate.Id
            }).HasName("ak_bird_attachments_farm_bird_id");
            attachment.Property(candidate => candidate.BreedingFarmId).IsRequired();
            attachment.Property(candidate => candidate.BirdId).IsRequired();
            attachment.Property(candidate => candidate.ObjectKey).HasMaxLength(500).IsRequired();
            attachment.Property(candidate => candidate.FileName).HasMaxLength(255).IsRequired();
            attachment.Property(candidate => candidate.ContentType).HasMaxLength(100).IsRequired();
            attachment.Property(candidate => candidate.Length).IsRequired();
            attachment.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            attachment.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            attachment.Property(candidate => candidate.DeletedAtUtc);
            attachment.Property(candidate => candidate.StorageCleanupPending)
                .HasDefaultValue(false)
                .IsRequired();
            attachment.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.BirdId,
                candidate.DeletedAtUtc,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_bird_attachments_farm_bird_created_at");
            attachment.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.ObjectKey
            })
                .IsUnique()
                .HasDatabaseName("ux_bird_attachments_farm_object_key");
            attachment.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.BirdId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BirdDocument>(document =>
        {
            document.ToTable("bird_documents", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_bird_documents_type_valid",
                    "\"Type\" IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_bird_documents_model_valid",
                    "\"ModelId\" IS NULL OR \"ModelId\" IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_bird_documents_print_size_valid",
                    "\"PrintSize\" IS NULL OR \"PrintSize\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_bird_documents_model_size_consistency",
                    "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 2 AND \"ModelId\" IS NULL AND \"PrintSize\" IS NULL)");
                table.HasCheckConstraint(
                    "ck_bird_documents_object_key_not_blank",
                    "btrim(\"ObjectKey\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_documents_file_name_not_blank",
                    "btrim(\"FileName\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_documents_content_type_not_blank",
                    "btrim(\"ContentType\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_documents_content_type_pdf",
                    "lower(\"ContentType\") = 'application/pdf'");
                table.HasCheckConstraint(
                    "ck_bird_documents_length_positive",
                    "\"Length\" > 0");
                table.HasCheckConstraint(
                    "ck_bird_documents_selected_fields_array",
                    "jsonb_typeof(\"SelectedFieldsJson\") = 'array'");
                table.HasCheckConstraint(
                    "ck_bird_documents_snapshot_object",
                    "jsonb_typeof(\"SnapshotJson\") = 'object'");
            });
            document.HasKey(candidate => candidate.Id);
            document.Property(candidate => candidate.BirdId).IsRequired();
            document.Property(candidate => candidate.CreatedByBreedingFarmId).IsRequired();
            document.Property(candidate => candidate.Type).HasConversion<int>().IsRequired();
            document.Property(candidate => candidate.ModelId).HasConversion<int>();
            document.Property(candidate => candidate.PrintSize).HasConversion<int>();
            document.Property(candidate => candidate.ObjectKey).HasMaxLength(500).IsRequired();
            document.Property(candidate => candidate.FileName).HasMaxLength(255).IsRequired();
            document.Property(candidate => candidate.ContentType).HasMaxLength(100).IsRequired();
            document.Property(candidate => candidate.Length).IsRequired();
            document.Property(candidate => candidate.GeneratedAtUtc).IsRequired();
            document.Property(candidate => candidate.SelectedFieldsJson)
                .HasColumnType("jsonb")
                .IsRequired();
            document.Property(candidate => candidate.SnapshotJson)
                .HasColumnType("jsonb")
                .IsRequired();
            document.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            document.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            document.Property<uint>("xmin").IsRowVersion();
            document.HasIndex(candidate => new { candidate.BirdId, candidate.GeneratedAtUtc })
                .HasDatabaseName("ix_bird_documents_bird_generated_at");
            document.HasIndex(candidate => new { candidate.CreatedByBreedingFarmId, candidate.CreatedAtUtc })
                .HasDatabaseName("ix_bird_documents_provenance_created_at");
            document.HasIndex(candidate => new { candidate.CreatedByBreedingFarmId, candidate.ObjectKey })
                .IsUnique()
                .HasDatabaseName("ux_bird_documents_provenance_object_key");
            document.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BirdId)
                .OnDelete(DeleteBehavior.Restrict);
            document.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.CreatedByBreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExternalTransfer>(externalTransfer =>
        {
            externalTransfer.ToTable("external_transfers", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_external_transfers_recipient_name_not_blank",
                    "btrim(\"RecipientName\") <> ''");
            });
            externalTransfer.HasKey(candidate => candidate.Id);
            externalTransfer.Property(candidate => candidate.BreedingFarmId).IsRequired();
            externalTransfer.Property(candidate => candidate.BirdId).IsRequired();
            externalTransfer.Property(candidate => candidate.RecipientName)
                .HasMaxLength(200)
                .IsRequired();
            externalTransfer.Property(candidate => candidate.Notes).HasMaxLength(2000);
            externalTransfer.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            externalTransfer.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            externalTransfer.HasIndex(candidate => candidate.BirdId)
                .IsUnique()
                .HasDatabaseName("ux_external_transfers_bird");
            externalTransfer.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            externalTransfer.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.BirdId })
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
