using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Billing;
using CriatorioVirtual.Domain.Billing;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Competitions;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Domain.Documents;
using SpeciesEntity = CriatorioVirtual.Domain.Species.Species;
using CriatorioVirtual.Infrastructure.Species;
using CriatorioVirtual.Application.BreedingFarms;
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

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    public DbSet<AsaasWebhookEventRecord> AsaasWebhookEvents => Set<AsaasWebhookEventRecord>();

    public DbSet<BreedingFarmUser> BreedingFarmUsers => Set<BreedingFarmUser>();

    public DbSet<Bird> Birds => Set<Bird>();

    public DbSet<BirdStatusTransition> BirdStatusTransitions => Set<BirdStatusTransition>();

    public DbSet<BirdCompetition> BirdCompetitions => Set<BirdCompetition>();

    public DbSet<GenealogyNode> GenealogyNodes => Set<GenealogyNode>();

    public DbSet<ExternalGenealogyNode> ExternalGenealogyNodes => Set<ExternalGenealogyNode>();

    public DbSet<ExternalGenealogyParentLink> ExternalGenealogyParentLinks => Set<ExternalGenealogyParentLink>();

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
            user.ToTable("users", "identity", table => table.HasCheckConstraint(
                "ck_users_avatar_reference_consistent",
                "(\"AvatarObjectKey\" IS NULL AND \"AvatarContentType\" IS NULL) OR " +
                "(\"AvatarObjectKey\" IS NOT NULL AND btrim(\"AvatarObjectKey\") <> '' AND " +
                "\"AvatarContentType\" IS NOT NULL AND " +
                "\"AvatarContentType\" IN ('image/jpeg', 'image/png'))"));
            user.Property(candidate => candidate.SelectedBreedingFarmId)
                .HasColumnName("SelectedBreedingFarmId");
            user.Property(candidate => candidate.AvatarObjectKey)
                .HasMaxLength(128);
            user.Property(candidate => candidate.AvatarContentType)
                .HasMaxLength(100);
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
                table.HasCheckConstraint(
                    "ck_breeding_farms_visual_identity_reference_source_pair",
                    "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityReference\" IS NULL) OR (\"VisualIdentitySource\" IS NOT NULL AND \"VisualIdentitySource\" IN (1, 2) AND \"VisualIdentityReference\" IS NOT NULL AND btrim(\"VisualIdentityReference\") <> '')");
                table.HasCheckConstraint(
                    "ck_breeding_farms_visual_identity_metadata",
                    "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL AND \"VisualIdentityTemplateModelId\" IS NULL AND \"VisualIdentityTemplateVersion\" IS NULL AND \"VisualIdentityTemplateConfiguration\" IS NULL) OR " +
                    "(\"VisualIdentitySource\" = 1 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" IN ('image/jpeg', 'image/png') AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760 AND \"VisualIdentityTemplateModelId\" IS NULL AND \"VisualIdentityTemplateVersion\" IS NULL AND \"VisualIdentityTemplateConfiguration\" IS NULL) OR " +
                    "(\"VisualIdentitySource\" = 2 AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL AND \"VisualIdentityTemplateModelId\" IS NULL AND \"VisualIdentityTemplateVersion\" IS NULL AND \"VisualIdentityTemplateConfiguration\" IS NULL) OR " +
                    "(\"VisualIdentitySource\" = 2 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" = 'image/png' AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760 AND \"VisualIdentityTemplateModelId\" IS NOT NULL AND btrim(\"VisualIdentityTemplateModelId\") <> '' AND \"VisualIdentityTemplateVersion\" IS NOT NULL AND btrim(\"VisualIdentityTemplateVersion\") <> '' AND \"VisualIdentityTemplateConfiguration\" IS NOT NULL AND jsonb_typeof(\"VisualIdentityTemplateConfiguration\") = 'object')");
                table.HasCheckConstraint(
                    "ck_breeding_farms_cover_reference_source_pair",
                    "(\"CoverSource\" IS NULL AND \"CoverReference\" IS NULL) OR (\"CoverSource\" IS NOT NULL AND \"CoverSource\" IN (1, 2) AND \"CoverReference\" IS NOT NULL AND btrim(\"CoverReference\") <> '')");
                table.HasCheckConstraint(
                    "ck_breeding_farms_cover_metadata",
                    "(\"CoverSource\" IS NULL AND \"CoverFileName\" IS NULL AND \"CoverContentType\" IS NULL AND \"CoverLength\" IS NULL AND \"CoverTemplateModelId\" IS NULL AND \"CoverTemplateVersion\" IS NULL AND \"CoverTemplateConfiguration\" IS NULL AND \"CoverUpdatedAtUtc\" IS NULL) OR " +
                    "(\"CoverSource\" = 1 AND \"CoverFileName\" IS NOT NULL AND btrim(\"CoverFileName\") <> '' AND \"CoverContentType\" = 'image/png' AND \"CoverLength\" > 0 AND \"CoverLength\" <= 20971520 AND \"CoverTemplateModelId\" IS NULL AND \"CoverTemplateVersion\" IS NULL AND \"CoverTemplateConfiguration\" IS NULL AND \"CoverUpdatedAtUtc\" IS NOT NULL) OR " +
                    "(\"CoverSource\" = 2 AND \"CoverFileName\" IS NOT NULL AND btrim(\"CoverFileName\") <> '' AND \"CoverContentType\" = 'image/png' AND \"CoverLength\" > 0 AND \"CoverLength\" <= 20971520 AND \"CoverTemplateModelId\" IS NOT NULL AND btrim(\"CoverTemplateModelId\") <> '' AND \"CoverTemplateVersion\" IS NOT NULL AND btrim(\"CoverTemplateVersion\") <> '' AND \"CoverTemplateConfiguration\" IS NOT NULL AND jsonb_typeof(\"CoverTemplateConfiguration\") = 'object' AND \"CoverUpdatedAtUtc\" IS NOT NULL)");
            });
            farm.HasKey(candidate => candidate.Id);
            farm.Property(candidate => candidate.Name).HasMaxLength(200).IsRequired();
            farm.Property(candidate => candidate.ResponsibleName).HasMaxLength(200).IsRequired();
            farm.Property(candidate => candidate.ContactEmail).HasMaxLength(320).IsRequired();
            farm.Property(candidate => candidate.ContactPhone).HasMaxLength(32);
            farm.Property(candidate => candidate.OfficialRegistrationNumber).HasMaxLength(100);
            farm.Property(candidate => candidate.VisualIdentityReference).HasMaxLength(500);
            farm.Property(candidate => candidate.VisualIdentitySource).HasConversion<int>();
            farm.Property(candidate => candidate.VisualIdentityFileName).HasMaxLength(255);
            farm.Property(candidate => candidate.VisualIdentityContentType).HasMaxLength(100);
            farm.Property(candidate => candidate.VisualIdentityTemplateModelId).HasMaxLength(100);
            farm.Property(candidate => candidate.VisualIdentityTemplateVersion).HasMaxLength(32);
            farm.Property(candidate => candidate.VisualIdentityTemplateConfiguration).HasColumnType("jsonb");
            farm.Property(candidate => candidate.CoverReference).HasMaxLength(500);
            farm.Property(candidate => candidate.CoverSource).HasConversion<int>();
            farm.Property(candidate => candidate.CoverFileName).HasMaxLength(255);
            farm.Property(candidate => candidate.CoverContentType).HasMaxLength(100);
            farm.Property(candidate => candidate.CoverTemplateModelId).HasMaxLength(100);
            farm.Property(candidate => candidate.CoverTemplateVersion).HasMaxLength(32);
            farm.Property(candidate => candidate.CoverTemplateConfiguration).HasColumnType("jsonb");
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

        modelBuilder.Entity<Subscription>(subscription =>
        {
            subscription.ToTable("subscriptions", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_subscriptions_billing_cycle_valid",
                    "\"BillingCycle\" IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_subscriptions_status_valid",
                    "\"Status\" IN (1, 2, 3, 4, 5, 6)");
                table.HasCheckConstraint(
                    "ck_subscriptions_plan_code_not_blank",
                    "btrim(\"PlanCode\") <> ''");
                table.HasCheckConstraint(
                    "ck_subscriptions_agreed_amount_positive",
                    "\"AgreedAmount\" IS NULL OR \"AgreedAmount\" > 0");
                table.HasCheckConstraint(
                    "ck_subscriptions_gateway_ids_consistent",
                    "(\"GatewayCustomerId\" IS NULL OR btrim(\"GatewayCustomerId\") <> '') AND (\"GatewaySubscriptionId\" IS NULL OR (\"GatewayCustomerId\" IS NOT NULL AND btrim(\"GatewaySubscriptionId\") <> ''))");
                table.HasCheckConstraint(
                    "ck_subscriptions_gateway_checkout_consistent",
                    "(\"GatewayCheckoutId\" IS NULL OR btrim(\"GatewayCheckoutId\") <> '') AND (\"GatewayCheckoutUrl\" IS NULL OR btrim(\"GatewayCheckoutUrl\") <> '') AND (\"GatewayCheckoutStatus\" IS NULL OR \"GatewayCheckoutStatus\" IN ('CREATING', 'ACTIVE', 'PAID', 'CANCELED', 'EXPIRED')) AND (\"GatewayCheckoutExpiresAtUtc\" IS NULL OR \"GatewayCheckoutId\" IS NOT NULL) AND (\"GatewayCheckoutStatusUpdatedAtUtc\" IS NULL OR \"GatewayCheckoutId\" IS NOT NULL OR \"GatewayCheckoutStatus\" = 'CREATING') AND (\"GatewayCheckoutCreationStartedAtUtc\" IS NULL OR \"Status\" = 1)");
                table.HasCheckConstraint(
                    "ck_subscriptions_trial_dates_consistent",
                    "(\"TrialStartedAtUtc\" IS NULL AND \"TrialEndsAtUtc\" IS NULL AND \"NextChargeDueAtUtc\" IS NULL) OR (\"TrialStartedAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" > \"TrialStartedAtUtc\" AND ((\"Status\" = 5 AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4, 6) AND \"NextChargeDueAtUtc\" IS NOT NULL AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")))");
                table.HasCheckConstraint(
                    "ck_subscriptions_trial_duration_exact",
                    "\"TrialStartedAtUtc\" IS NULL OR \"TrialEndsAtUtc\" - \"TrialStartedAtUtc\" = INTERVAL '168 hours'");
                table.HasCheckConstraint(
                    "ck_subscriptions_charge_due_matches_status",
                    "(\"Status\" IN (1, 5) AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"NextChargeDueAtUtc\" = \"TrialEndsAtUtc\") OR (\"Status\" = 3 AND \"NextChargeDueAtUtc\" > \"TrialEndsAtUtc\") OR (\"Status\" IN (4, 6) AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")");
                table.HasCheckConstraint(
                    "ck_subscriptions_trial_requires_gateway_confirmation",
                    "(\"Status\" = 1 AND \"GatewaySubscriptionId\" IS NULL AND \"TrialStartedAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4, 5, 6) AND \"GatewaySubscriptionId\" IS NOT NULL AND \"TrialStartedAtUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_subscriptions_grace_period_dates_consistent",
                    "(\"Status\" IN (4, 6) AND \"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours') OR (\"Status\" = 5 AND ((\"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL) OR (\"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours'))) OR (\"Status\" IN (1, 2, 3) AND \"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL)");
            });
            subscription.HasKey(candidate => candidate.Id);
            subscription.HasAlternateKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .HasName("ak_subscriptions_farm_id");
            subscription.Property(candidate => candidate.BreedingFarmId).IsRequired();
            subscription.Property(candidate => candidate.PlanCode)
                .HasMaxLength(Subscription.PlanCodeMaxLength)
                .IsRequired();
            subscription.Property(candidate => candidate.BillingCycle).HasConversion<int>().IsRequired();
            subscription.Property(candidate => candidate.AgreedAmount).HasPrecision(18, 2);
            subscription.Property(candidate => candidate.Status).HasConversion<int>().IsRequired();
            subscription.Property(candidate => candidate.GatewayCustomerId).HasMaxLength(Subscription.GatewayIdMaxLength);
            subscription.Property(candidate => candidate.GatewaySubscriptionId).HasMaxLength(Subscription.GatewayIdMaxLength);
            subscription.Property(candidate => candidate.GatewayCheckoutId).HasMaxLength(Subscription.GatewayIdMaxLength);
            subscription.Property(candidate => candidate.GatewayCheckoutUrl).HasMaxLength(2048);
            subscription.Property(candidate => candidate.GatewayCheckoutStatus).HasMaxLength(32);
            subscription.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            subscription.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            subscription.Property<uint>("xmin").IsRowVersion();
            subscription.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            subscription.HasIndex(candidate => candidate.BreedingFarmId)
                .IsUnique()
                .HasDatabaseName("ux_subscriptions_active_per_breeding_farm")
                .HasFilter("\"Status\" IN (2, 3, 4, 6)");
            subscription.HasIndex(
                    candidate => candidate.BreedingFarmId,
                    "IX_Subscriptions_PendingPerBreedingFarm")
                .IsUnique()
                .HasDatabaseName("ux_subscriptions_pending_per_breeding_farm")
                .HasFilter("\"Status\" = 1");
            subscription.HasIndex(candidate => candidate.GatewaySubscriptionId)
                .IsUnique()
                .HasDatabaseName("ux_subscriptions_gateway_subscription_id")
                .HasFilter("\"GatewaySubscriptionId\" IS NOT NULL");
            subscription.HasIndex(candidate => candidate.GatewayCheckoutId)
                .IsUnique()
                .HasDatabaseName("ux_subscriptions_gateway_checkout_id")
                .HasFilter("\"GatewayCheckoutId\" IS NOT NULL");
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.ToTable("payments", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_status_valid",
                    "\"Status\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_payments_gateway_payment_id_not_blank",
                    "btrim(\"GatewayPaymentId\") <> ''");
                table.HasCheckConstraint(
                    "ck_payments_amount_positive",
                    "\"Amount\" > 0");
                table.HasCheckConstraint(
                    "ck_payments_currency_valid",
                    "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint(
                    "ck_payments_confirmation_consistent",
                    "(\"Status\" = 2 AND \"PaidAtUtc\" IS NOT NULL) OR (\"Status\" <> 2 AND \"PaidAtUtc\" IS NULL)");
            });
            payment.HasKey(candidate => candidate.Id);
            payment.Property(candidate => candidate.BreedingFarmId).IsRequired();
            payment.Property(candidate => candidate.SubscriptionId).IsRequired();
            payment.Property(candidate => candidate.GatewayPaymentId)
                .HasMaxLength(Subscription.GatewayIdMaxLength)
                .IsRequired();
            payment.Property(candidate => candidate.Amount).HasPrecision(18, 2).IsRequired();
            payment.Property(candidate => candidate.CurrencyCode).HasMaxLength(Payment.CurrencyCodeLength).IsRequired();
            payment.Property(candidate => candidate.Status).HasConversion<int>().IsRequired();
            payment.Property(candidate => candidate.DueAtUtc).IsRequired();
            payment.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            payment.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            payment.Property<uint>("xmin").IsRowVersion();
            payment.HasOne<Subscription>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.SubscriptionId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Restrict);
            payment.HasIndex(candidate => candidate.GatewayPaymentId)
                .IsUnique()
                .HasDatabaseName("ux_payments_gateway_payment_id");
            payment.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.SubscriptionId,
                candidate.CreatedAtUtc,
                candidate.Id
            })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_payments_farm_subscription_created");
        });

        modelBuilder.Entity<PaymentAttempt>(attempt =>
        {
            attempt.ToTable("payment_attempts", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_payment_attempts_status_valid",
                    "\"Status\" IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_payment_attempts_idempotency_key_not_empty",
                    "\"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "ck_payment_attempts_request_fingerprint_valid",
                    "\"RequestFingerprint\" ~ '^[A-F0-9]{64}$'");
            });
            attempt.HasKey(candidate => candidate.Id);
            attempt.Property(candidate => candidate.Id).ValueGeneratedNever();
            attempt.Property(candidate => candidate.PaymentId).IsRequired();
            attempt.Property(candidate => candidate.IdempotencyKey).IsRequired();
            attempt.Property(candidate => candidate.RequestFingerprint)
                .HasMaxLength(PaymentAttempt.RequestFingerprintLength)
                .IsRequired();
            attempt.Property(candidate => candidate.Status).HasConversion<int>().IsRequired();
            attempt.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            attempt.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            attempt.Property<uint>("xmin").IsRowVersion();
            attempt.HasOne<Payment>()
                .WithMany()
                .HasForeignKey(candidate => candidate.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            attempt.HasIndex(candidate => new { candidate.PaymentId, candidate.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("ux_payment_attempts_payment_idempotency_key");
            attempt.HasIndex(candidate => new { candidate.PaymentId, candidate.Status })
                .HasDatabaseName("ix_payment_attempts_payment_status");
        });

        modelBuilder.Entity<AsaasWebhookEventRecord>(webhookEvent =>
        {
            webhookEvent.ToTable("asaas_webhook_events", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_asaas_webhook_events_provider_event_id_not_blank",
                    "btrim(\"ProviderEventId\") <> ''");
                table.HasCheckConstraint(
                    "ck_asaas_webhook_events_event_type_not_blank",
                    "btrim(\"EventType\") <> ''");
                table.HasCheckConstraint(
                    "ck_asaas_webhook_events_payload_object",
                    "jsonb_typeof(\"Payload\") = 'object'");
            });
            webhookEvent.HasKey(candidate => candidate.Id);
            webhookEvent.Property(candidate => candidate.Id).ValueGeneratedNever();
            webhookEvent.Property(candidate => candidate.ProviderEventId)
                .HasMaxLength(AsaasWebhookEventRecord.ProviderEventIdMaxLength)
                .IsRequired();
            webhookEvent.Property(candidate => candidate.EventType)
                .HasMaxLength(AsaasWebhookEventRecord.EventTypeMaxLength)
                .IsRequired();
            webhookEvent.Property(candidate => candidate.Payload)
                .HasColumnType("jsonb")
                .IsRequired();
            webhookEvent.Property(candidate => candidate.ReceivedAtUtc).IsRequired();
            webhookEvent.Property(candidate => candidate.ProcessedAtUtc);
            webhookEvent.Property(candidate => candidate.ProcessingAttempts).IsRequired();
            webhookEvent.Property(candidate => candidate.NextAttemptAtUtc).IsRequired();
            webhookEvent.HasIndex(candidate => candidate.ProviderEventId)
                .IsUnique()
                .HasDatabaseName("ux_asaas_webhook_events_provider_event_id");
            webhookEvent.HasIndex(candidate => new
                {
                    candidate.NextAttemptAtUtc,
                    candidate.ReceivedAtUtc
                })
                .HasDatabaseName("ix_asaas_webhook_events_retry")
                .HasFilter("\"ProcessedAtUtc\" IS NULL");
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
                table.HasCheckConstraint(
                    "ck_birds_default_image_metadata_pair",
                    "(\"DefaultImageFileName\" IS NULL) = (\"DefaultImageContentType\" IS NULL)");
            });
            bird.HasKey(candidate => candidate.Id);
            bird.HasAlternateKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .HasName("ak_birds_farm_id");
            bird.Property(candidate => candidate.BreedingFarmId).IsRequired();
            bird.Property(candidate => candidate.PrimaryPhotoId);
            bird.Property(candidate => candidate.DefaultImageFileName).HasMaxLength(255);
            bird.Property(candidate => candidate.DefaultImageContentType).HasMaxLength(100);
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
            bird.HasIndex(candidate => new { candidate.BreedingFarmId, candidate.CreatedAtUtc })
                .HasDatabaseName("ix_birds_farm_created_at");
            bird.HasIndex(candidate => new { candidate.BreedingFarmId, candidate.BirthDate })
                .HasDatabaseName("ix_birds_farm_birth_date")
                .HasFilter("\"BirthDate\" IS NOT NULL");
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
            bird.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.Id,
                candidate.PrimaryPhotoId
            }).HasDatabaseName("IX_birds_BreedingFarmId_Id_PrimaryPhotoId");
        });

        modelBuilder.Entity<BirdStatusTransition>(transition =>
        {
            transition.ToTable("bird_status_transitions", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_bird_status_transitions_from_status_valid",
                    "\"FromStatus\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_bird_status_transitions_to_status_valid",
                    "\"ToStatus\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_bird_status_transitions_status_changed",
                    "\"FromStatus\" <> \"ToStatus\"");
            });
            transition.HasKey(candidate => candidate.Id);
            transition.Property(candidate => candidate.BreedingFarmId).IsRequired();
            transition.Property(candidate => candidate.BirdId).IsRequired();
            transition.Property(candidate => candidate.ChangedByUserId).IsRequired();
            transition.Property(candidate => candidate.FromStatus)
                .HasConversion<int>()
                .IsRequired();
            transition.Property(candidate => candidate.ToStatus)
                .HasConversion<int>()
                .IsRequired();
            transition.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            transition.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            transition.HasIndex(candidate => new
            {
                candidate.BirdId,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_bird_status_transitions_bird_created_at");
            transition.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_bird_status_transitions_farm_created_at");
            transition.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BirdId)
                .OnDelete(DeleteBehavior.Restrict);
            transition.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            transition.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(candidate => candidate.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BirdCompetition>(competition =>
        {
            competition.ToTable("bird_competitions", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_bird_competitions_name_not_blank",
                    "btrim(\"Name\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_competitions_placement_positive",
                    "\"Placement\" IS NULL OR \"Placement\" > 0");
            });
            competition.HasKey(candidate => candidate.Id);
            competition.Property(candidate => candidate.BreedingFarmId).IsRequired();
            competition.Property(candidate => candidate.BirdId).IsRequired();
            competition.Property(candidate => candidate.Name)
                .HasMaxLength(BirdCompetition.NameMaxLength)
                .IsRequired();
            competition.Property(candidate => candidate.CompetitionDate)
                .HasColumnType("date");
            competition.Property(candidate => candidate.Category)
                .HasMaxLength(BirdCompetition.CategoryMaxLength);
            competition.Property(candidate => candidate.Placement);
            competition.Property(candidate => candidate.Location)
                .HasMaxLength(BirdCompetition.LocationMaxLength);
            competition.Property(candidate => candidate.Notes)
                .HasMaxLength(BirdCompetition.NotesMaxLength);
            competition.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            competition.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            competition.Property<uint>("xmin").IsRowVersion();
            competition.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.BirdId,
                candidate.CompetitionDate,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_bird_competitions_farm_bird_date_created_at");
            competition.HasOne<BreedingFarm>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BreedingFarmId)
                .OnDelete(DeleteBehavior.Restrict);
            // Competition history remains owned by the farm where it was recorded,
            // even when the referenced bird later moves to another farm.
            competition.HasOne<Bird>()
                .WithMany()
                .HasForeignKey(candidate => candidate.BirdId)
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
                candidate.StartDate
            }).HasDatabaseName("ix_reproductions_farm_start_date");
            reproduction.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.Status,
                candidate.EndDate
            }).HasDatabaseName("ix_reproductions_farm_status_end_date");
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
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_bird_snapshot_name_not_blank",
                    "btrim(\"BirdSnapshotName\") <> ''");
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_bird_snapshot_sex_valid",
                    "\"BirdSnapshotSex\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_bird_snapshot_status_valid",
                    "\"BirdSnapshotStatus\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_internal_transfer_requests_bird_snapshot_ring_number_format",
                    "\"BirdSnapshotRingNumber\" IS NULL OR \"BirdSnapshotRingNumber\" ~ '^[0-9]{6}$'");
            });
            transferRequest.HasKey(candidate => candidate.Id);
            transferRequest.Property(candidate => candidate.SourceBreedingFarmId).IsRequired();
            transferRequest.Property(candidate => candidate.DestinationBreedingFarmId).IsRequired();
            transferRequest.Property(candidate => candidate.BirdId).IsRequired();
            transferRequest.Property(candidate => candidate.RequestedByUserId).IsRequired();
            transferRequest.Property(candidate => candidate.BirdSnapshotName)
                .HasMaxLength(100)
                .IsRequired();
            transferRequest.Property(candidate => candidate.BirdSnapshotSex)
                .HasConversion<int>()
                .IsRequired();
            transferRequest.Property(candidate => candidate.BirdSnapshotRingNumber)
                .HasMaxLength(6);
            transferRequest.Property(candidate => candidate.BirdSnapshotStatus)
                .HasConversion<int>()
                .IsRequired();
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
            transferRequest.HasIndex(candidate => new
            {
                candidate.SourceBreedingFarmId,
                candidate.Status,
                candidate.UpdatedAtUtc
            }).HasDatabaseName("ix_internal_transfer_requests_source_status_updated_at");
            transferRequest.HasIndex(candidate => new
            {
                candidate.DestinationBreedingFarmId,
                candidate.Status,
                candidate.UpdatedAtUtc
            }).HasDatabaseName("ix_internal_transfer_requests_destination_status_updated_at");
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
                    "ck_bird_attachments_caption_not_blank",
                    "\"Caption\" IS NULL OR btrim(\"Caption\") <> ''");
                table.HasCheckConstraint(
                    "ck_bird_attachments_cleanup_requires_deletion",
                    "\"StorageCleanupPending\" = FALSE OR \"DeletedAtUtc\" IS NOT NULL");
            });
            attachment.HasKey(candidate => candidate.Id);
            attachment.Property(candidate => candidate.BreedingFarmId).IsRequired();
            attachment.Property(candidate => candidate.ObjectKey).HasMaxLength(500).IsRequired();
            attachment.Property(candidate => candidate.FileName).HasMaxLength(255).IsRequired();
            attachment.Property(candidate => candidate.ContentType).HasMaxLength(100).IsRequired();
            attachment.Property(candidate => candidate.Length).IsRequired();
            attachment.Property(candidate => candidate.Caption).HasMaxLength(BirdAttachment.CaptionMaxLength);
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
            // Keep the same-bird primary-photo FK in PostgreSQL without making BirdId required.
            // EF alternate keys force nullable key properties to become required, so the FK is
            // installed by the migration against this nullable unique index instead.
            attachment.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.BirdId,
                candidate.Id
            }).IsUnique().HasDatabaseName("ux_bird_attachments_farm_bird_id");
            attachment.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.CreatedAtUtc,
                candidate.Id
            })
                .HasDatabaseName("ix_bird_attachments_farm_created_media")
                .HasFilter("\"DeletedAtUtc\" IS NULL");
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
                    "\"Type\" IN (1, 3, 4)");
                table.HasCheckConstraint(
                    "ck_bird_documents_model_valid",
                    "\"ModelId\" IS NULL OR \"ModelId\" IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "ck_bird_documents_certificate_model_valid",
                    "\"CertificateModelId\" IS NULL OR \"CertificateModelId\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_bird_documents_print_size_valid",
                    "\"PrintSize\" IS NULL OR \"PrintSize\" IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "ck_bird_documents_model_size_consistency",
                    "(\"Type\" = 1 AND \"ModelId\" IS NOT NULL AND \"CertificateModelId\" IS NULL AND \"PrintSize\" IS NOT NULL) OR (\"Type\" = 3 AND \"ModelId\" IS NULL AND \"CertificateModelId\" IS NOT NULL AND \"PrintSize\" IS NULL) OR (\"Type\" = 4 AND \"ModelId\" IS NULL AND \"CertificateModelId\" IS NULL AND \"PrintSize\" IS NULL)");
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
            document.Property(candidate => candidate.CertificateModelId).HasConversion<int>();
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
            externalTransfer.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.CreatedAtUtc
            }).HasDatabaseName("ix_external_transfers_farm_created_at");
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
            node.HasAlternateKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .HasName("ak_genealogy_nodes_breeding_farm_id");
            node.HasAlternateKey(candidate => new { candidate.BreedingFarmId, candidate.Id, candidate.BirdId })
                .HasName("ak_genealogy_nodes_tree_bird");
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

        modelBuilder.Entity<ExternalGenealogyNode>(node =>
        {
            node.ToTable("external_genealogy_nodes", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_external_genealogy_nodes_name_not_blank",
                    "btrim(\"Name\") <> ''");
                table.HasCheckConstraint(
                    "ck_external_genealogy_nodes_sex_valid",
                    "\"Sex\" IN (1, 2)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_nodes_snapshot_consistency",
                    "(\"IsBirdSnapshot\" = FALSE AND \"SnapshotSourceBirdId\" IS NULL AND \"SnapshotBirthDate\" IS NULL AND \"SnapshotRingNumber\" IS NULL AND \"SnapshotStatus\" IS NULL AND \"CanNavigateToSourceBird\" = FALSE) OR (\"IsBirdSnapshot\" = TRUE AND \"SnapshotStatus\" IS NOT NULL AND (\"CanNavigateToSourceBird\" = FALSE OR \"SnapshotSourceBirdId\" IS NOT NULL))");
                table.HasCheckConstraint(
                    "ck_external_genealogy_nodes_snapshot_status_valid",
                    "\"SnapshotStatus\" IS NULL OR \"SnapshotStatus\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_nodes_snapshot_ring_number_format",
                    "\"SnapshotRingNumber\" IS NULL OR \"SnapshotRingNumber\" ~ '^[0-9]{6}$'");
            });
            node.HasKey(candidate => candidate.Id);
            node.Property(candidate => candidate.BreedingFarmId).IsRequired();
            node.Property(candidate => candidate.GenealogyRootId).IsRequired();
            node.Property(candidate => candidate.Name)
                .HasMaxLength(ExternalGenealogyNode.NameMaxLength)
                .IsRequired();
            node.Property(candidate => candidate.Sex)
                .HasConversion<int>()
                .IsRequired();
            node.Property(candidate => candidate.IsBirdSnapshot).IsRequired();
            node.Property(candidate => candidate.SnapshotSourceBirdId);
            node.Property(candidate => candidate.SnapshotBirthDate).HasColumnType("date");
            node.Property(candidate => candidate.SnapshotRingNumber).HasMaxLength(6);
            node.Property(candidate => candidate.SnapshotStatus).HasConversion<int>();
            node.Property(candidate => candidate.CanNavigateToSourceBird).IsRequired();
            node.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            node.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            node.Property<uint>("xmin").IsRowVersion();
            node.HasAlternateKey(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.GenealogyRootId,
                candidate.Id
            }).HasName("ak_external_genealogy_nodes_tree_id");
            node.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.GenealogyRootId
            }).HasDatabaseName("ix_external_genealogy_nodes_tree");
            node.HasOne<GenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.GenealogyRootId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalGenealogyParentLink>(link =>
        {
            link.ToTable("external_genealogy_parent_links", DefaultSchema, table =>
            {
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_child_source",
                    "(\"ChildBirdId\" IS NOT NULL AND \"ChildExternalNodeId\" IS NULL) OR (\"ChildBirdId\" IS NULL AND \"ChildExternalNodeId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_parent_source",
                    "(\"ParentBirdId\" IS NOT NULL AND \"ParentExternalNodeId\" IS NULL) OR (\"ParentBirdId\" IS NULL AND \"ParentExternalNodeId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_position_valid",
                    "\"Position\" IN ('father', 'mother')");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_snapshot_consistency",
                    "(\"ParentBirdId\" IS NULL AND \"ParentSnapshotName\" IS NULL AND \"ParentSnapshotSex\" IS NULL AND \"ParentSnapshotBirthDate\" IS NULL AND \"ParentSnapshotRingNumber\" IS NULL AND \"ParentSnapshotStatus\" IS NULL) OR (\"ParentBirdId\" IS NOT NULL AND \"ParentSnapshotName\" IS NOT NULL AND \"ParentSnapshotSex\" IS NOT NULL AND \"ParentSnapshotStatus\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_snapshot_sex_valid",
                    "\"ParentBirdId\" IS NULL OR (\"Position\" = 'father' AND \"ParentSnapshotSex\" = 1) OR (\"Position\" = 'mother' AND \"ParentSnapshotSex\" = 2)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_snapshot_status_valid",
                    "\"ParentSnapshotStatus\" IS NULL OR \"ParentSnapshotStatus\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_snapshot_ring_number_format",
                    "\"ParentSnapshotRingNumber\" IS NULL OR \"ParentSnapshotRingNumber\" ~ '^[0-9]{6}$'");
                table.HasCheckConstraint(
                    "ck_external_genealogy_parent_links_external_not_self",
                    "\"ChildExternalNodeId\" IS NULL OR \"ParentExternalNodeId\" IS NULL OR \"ChildExternalNodeId\" <> \"ParentExternalNodeId\"");
            });
            link.HasKey(candidate => candidate.Id);
            link.Property(candidate => candidate.BreedingFarmId).IsRequired();
            link.Property(candidate => candidate.GenealogyRootId).IsRequired();
            link.Property(candidate => candidate.Position).HasMaxLength(10).IsRequired();
            link.Property(candidate => candidate.ParentSnapshotName).HasMaxLength(200);
            link.Property(candidate => candidate.ParentSnapshotSex).HasConversion<int>();
            link.Property(candidate => candidate.ParentSnapshotBirthDate).HasColumnType("date");
            link.Property(candidate => candidate.ParentSnapshotRingNumber).HasMaxLength(6);
            link.Property(candidate => candidate.ParentSnapshotStatus).HasConversion<int>();
            link.Property(candidate => candidate.CreatedAtUtc).IsRequired();
            link.Property(candidate => candidate.UpdatedAtUtc).IsRequired();
            link.Property<uint>("xmin").IsRowVersion();
            link.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.GenealogyRootId,
                candidate.ChildBirdId,
                candidate.Position
            })
                .IsUnique()
                .HasDatabaseName("ux_external_genealogy_parent_links_bird_position")
                .HasFilter("\"ChildBirdId\" IS NOT NULL");
            link.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.GenealogyRootId,
                candidate.ChildExternalNodeId,
                candidate.Position
            })
                .IsUnique()
                .HasDatabaseName("ux_external_genealogy_parent_links_external_position")
                .HasFilter("\"ChildExternalNodeId\" IS NOT NULL");
            link.HasIndex(candidate => new
            {
                candidate.BreedingFarmId,
                candidate.GenealogyRootId,
                candidate.ParentExternalNodeId
            }).HasDatabaseName("ix_external_genealogy_parent_links_external_parent");
            link.HasOne<GenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => new { candidate.BreedingFarmId, candidate.GenealogyRootId })
                .HasPrincipalKey(candidate => new { candidate.BreedingFarmId, candidate.Id })
                .OnDelete(DeleteBehavior.Cascade);
            link.HasOne<GenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.GenealogyRootId,
                    candidate.ChildBirdId
                })
                .HasPrincipalKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.Id,
                    candidate.BirdId
                })
                .OnDelete(DeleteBehavior.Cascade);
            link.HasOne<ExternalGenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.GenealogyRootId,
                    candidate.ChildExternalNodeId
                })
                .HasPrincipalKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.GenealogyRootId,
                    candidate.Id
                })
                .OnDelete(DeleteBehavior.Cascade);
            link.HasOne<ExternalGenealogyNode>()
                .WithMany()
                .HasForeignKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.GenealogyRootId,
                    candidate.ParentExternalNodeId
                })
                .HasPrincipalKey(candidate => new
                {
                    candidate.BreedingFarmId,
                    candidate.GenealogyRootId,
                    candidate.Id
                })
                .OnDelete(DeleteBehavior.Restrict);
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
                table.HasCheckConstraint(
                    "ck_species_default_image_metadata_pair",
                    "(\"DefaultImageFileName\" IS NULL) = (\"DefaultImageContentType\" IS NULL)");
            });
            species.HasKey(candidate => candidate.Id);
            species.Property(candidate => candidate.ScientificName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.PopularName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.NormalizedScientificName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.NormalizedPopularName).HasMaxLength(200).IsRequired();
            species.Property(candidate => candidate.IsActive).IsRequired();
            species.Property(candidate => candidate.DefaultImageFileName).HasMaxLength(255);
            species.Property(candidate => candidate.DefaultImageContentType).HasMaxLength(100);
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
