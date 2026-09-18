using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedSubscriptionCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_gateway_ids_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GatewayCheckoutCreationStartedAtUtc",
                schema: "app",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GatewayCheckoutExpiresAtUtc",
                schema: "app",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayCheckoutId",
                schema: "app",
                table: "subscriptions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayCheckoutStatus",
                schema: "app",
                table: "subscriptions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GatewayCheckoutStatusUpdatedAtUtc",
                schema: "app",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayCheckoutUrl",
                schema: "app",
                table: "subscriptions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HostedCheckoutRequestedAtUtc",
                schema: "app",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_gateway_checkout_id",
                schema: "app",
                table: "subscriptions",
                column: "GatewayCheckoutId",
                unique: true,
                filter: "\"GatewayCheckoutId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_gateway_checkout_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"GatewayCheckoutId\" IS NULL OR btrim(\"GatewayCheckoutId\") <> '') AND (\"GatewayCheckoutUrl\" IS NULL OR btrim(\"GatewayCheckoutUrl\") <> '') AND (\"GatewayCheckoutStatus\" IS NULL OR \"GatewayCheckoutStatus\" IN ('CREATING', 'ACTIVE', 'PAID', 'CANCELED', 'EXPIRED')) AND (\"GatewayCheckoutExpiresAtUtc\" IS NULL OR \"GatewayCheckoutId\" IS NOT NULL) AND (\"GatewayCheckoutStatusUpdatedAtUtc\" IS NULL OR \"GatewayCheckoutId\" IS NOT NULL OR \"GatewayCheckoutStatus\" = 'CREATING') AND (\"GatewayCheckoutCreationStartedAtUtc\" IS NULL OR \"Status\" = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_gateway_ids_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"GatewayCustomerId\" IS NULL OR btrim(\"GatewayCustomerId\") <> '') AND (\"GatewaySubscriptionId\" IS NULL OR (\"GatewayCustomerId\" IS NOT NULL AND btrim(\"GatewaySubscriptionId\") <> ''))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_subscriptions_gateway_checkout_id",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_gateway_checkout_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_gateway_ids_consistent",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutCreationStartedAtUtc",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutExpiresAtUtc",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutId",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutStatus",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutStatusUpdatedAtUtc",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "GatewayCheckoutUrl",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "HostedCheckoutRequestedAtUtc",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_gateway_ids_consistent",
                schema: "app",
                table: "subscriptions",
                sql: "(\"GatewayCustomerId\" IS NULL AND \"GatewaySubscriptionId\" IS NULL) OR (\"GatewayCustomerId\" IS NOT NULL AND btrim(\"GatewayCustomerId\") <> '' AND \"GatewaySubscriptionId\" IS NOT NULL AND btrim(\"GatewaySubscriptionId\") <> '')");
        }
    }
}
