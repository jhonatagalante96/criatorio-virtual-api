using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlockExpiredSubscriptionGracePeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_subscriptions_active_per_breeding_farm",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_charge_due_matches_status",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_grace_period_dates_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_status_valid",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_trial_dates_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_trial_requires_gateway_confirmation",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_active_per_breeding_farm",
                schema: "app",
                table: "subscriptions",
                column: "BreedingFarmId",
                unique: true,
                filter: "\"Status\" IN (2, 3, 4, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_charge_due_matches_status",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" IN (1, 5) AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"NextChargeDueAtUtc\" = \"TrialEndsAtUtc\") OR (\"Status\" = 3 AND \"NextChargeDueAtUtc\" > \"TrialEndsAtUtc\") OR (\"Status\" IN (4, 6) AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_grace_period_dates_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" IN (4, 6) AND \"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours') OR (\"Status\" = 5 AND ((\"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL) OR (\"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours'))) OR (\"Status\" IN (1, 2, 3) AND \"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_status_valid",
                schema: "app",
                table: "subscriptions",
                sql: "\"Status\" IN (1, 2, 3, 4, 5, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_trial_dates_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"TrialStartedAtUtc\" IS NULL AND \"TrialEndsAtUtc\" IS NULL AND \"NextChargeDueAtUtc\" IS NULL) OR (\"TrialStartedAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" > \"TrialStartedAtUtc\" AND ((\"Status\" = 5 AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4, 6) AND \"NextChargeDueAtUtc\" IS NOT NULL AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_trial_requires_gateway_confirmation",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" = 1 AND \"GatewaySubscriptionId\" IS NULL AND \"TrialStartedAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4, 5, 6) AND \"GatewaySubscriptionId\" IS NOT NULL AND \"TrialStartedAtUtc\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_subscriptions_active_per_breeding_farm",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_charge_due_matches_status",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_grace_period_dates_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_status_valid",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_trial_dates_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_trial_requires_gateway_confirmation",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_active_per_breeding_farm",
                schema: "app",
                table: "subscriptions",
                column: "BreedingFarmId",
                unique: true,
                filter: "\"Status\" IN (2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_charge_due_matches_status",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" IN (1, 5) AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" IN (2, 4) AND \"NextChargeDueAtUtc\" = \"TrialEndsAtUtc\") OR (\"Status\" = 3 AND \"NextChargeDueAtUtc\" > \"TrialEndsAtUtc\")");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_grace_period_dates_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" = 4 AND \"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours') OR (\"Status\" = 5 AND ((\"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL) OR (\"GracePeriodStartedAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" IS NOT NULL AND \"GracePeriodEndsAtUtc\" - \"GracePeriodStartedAtUtc\" = INTERVAL '168 hours'))) OR (\"Status\" IN (1, 2, 3) AND \"GracePeriodStartedAtUtc\" IS NULL AND \"GracePeriodEndsAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_status_valid",
                schema: "app",
                table: "subscriptions",
                sql: "\"Status\" IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_trial_dates_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"TrialStartedAtUtc\" IS NULL AND \"TrialEndsAtUtc\" IS NULL AND \"NextChargeDueAtUtc\" IS NULL) OR (\"TrialStartedAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" IS NOT NULL AND \"TrialEndsAtUtc\" > \"TrialStartedAtUtc\" AND ((\"Status\" = 5 AND \"NextChargeDueAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4) AND \"NextChargeDueAtUtc\" IS NOT NULL AND \"NextChargeDueAtUtc\" >= \"TrialEndsAtUtc\")))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_trial_requires_gateway_confirmation",
                schema: "app",
                table: "subscriptions",
                sql: "(\"Status\" = 1 AND \"GatewaySubscriptionId\" IS NULL AND \"TrialStartedAtUtc\" IS NULL) OR (\"Status\" IN (2, 3, 4, 5) AND \"GatewaySubscriptionId\" IS NOT NULL AND \"TrialStartedAtUtc\" IS NOT NULL)");
        }
    }
}
