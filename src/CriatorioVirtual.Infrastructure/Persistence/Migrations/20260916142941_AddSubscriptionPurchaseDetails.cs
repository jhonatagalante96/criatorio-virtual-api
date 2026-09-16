using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPurchaseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AgreedAmount",
                schema: "app",
                table: "subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_pending_per_breeding_farm",
                schema: "app",
                table: "subscriptions",
                column: "BreedingFarmId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_agreed_amount_positive",
                schema: "app",
                table: "subscriptions",
                sql: "\"AgreedAmount\" IS NULL OR \"AgreedAmount\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_subscriptions_pending_per_breeding_farm",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_agreed_amount_positive",
                schema: "app",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "AgreedAmount",
                schema: "app",
                table: "subscriptions");
        }
    }
}
