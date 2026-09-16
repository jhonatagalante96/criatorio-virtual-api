using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAndPaymentModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BillingCycle = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GatewayCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GatewaySubscriptionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TrialStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TrialEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextChargeDueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GracePeriodStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                    table.UniqueConstraint("ak_subscriptions_farm_id", x => new { x.BreedingFarmId, x.Id });
                    table.CheckConstraint("ck_subscriptions_billing_cycle_valid", "\"BillingCycle\" IN (1, 2)");
                    table.CheckConstraint("ck_subscriptions_gateway_ids_consistent", "(\"GatewayCustomerId\" IS NULL AND \"GatewaySubscriptionId\" IS NULL) OR (\"GatewayCustomerId\" IS NOT NULL AND btrim(\"GatewayCustomerId\") <> '' AND \"GatewaySubscriptionId\" IS NOT NULL AND btrim(\"GatewaySubscriptionId\") <> '')");
                    table.CheckConstraint("ck_subscriptions_grace_period_dates_consistent", "(\"Status\" = 4 AND \"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" > \"GracePeriodStartedAtUtc\") OR (\"Status\" <> 4 AND \"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL)");
                    table.CheckConstraint("ck_subscriptions_status_valid", "\"Status\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_subscriptions_trial_dates_consistent", "(\"TrialStartedAtUtc\" IS NULL AND \"TrialEndsAtUtc\" IS NULL AND \"NextChargeDueAtUtc\" IS NULL) OR (\"TrialStartedAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" IS NOT NULL AND \"NextChargeDueAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" > \"TrialStartedAtUtc\" AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")");
                    table.CheckConstraint("ck_subscriptions_trial_requires_gateway_confirmation", "\"Status\" = 1 OR (\"GatewaySubscriptionId\" IS NOT NULL AND \"TrialStartedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_subscriptions_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payments_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("ck_payments_confirmation_consistent", "(\"Status\" = 2 AND \"PaidAtUtc\" IS NOT NULL) OR (\"Status\" <> 2 AND \"PaidAtUtc\" IS NULL)");
                    table.CheckConstraint("ck_payments_currency_valid", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_payments_status_valid", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_payments_subscriptions_BreedingFarmId_SubscriptionId",
                        columns: x => new { x.BreedingFarmId, x.SubscriptionId },
                        principalSchema: "app",
                        principalTable: "subscriptions",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_farm_subscription_created",
                schema: "app",
                table: "payments",
                columns: new[] { "BreedingFarmId", "SubscriptionId", "CreatedAtUtc", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_payments_gateway_payment_id",
                schema: "app",
                table: "payments",
                column: "GatewayPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_gateway_subscription_id",
                schema: "app",
                table: "subscriptions",
                column: "GatewaySubscriptionId",
                unique: true,
                filter: "\"GatewaySubscriptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_trial_per_breeding_farm",
                schema: "app",
                table: "subscriptions",
                column: "BreedingFarmId",
                unique: true,
                filter: "\"TrialStartedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "app");
        }
    }
}
