using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmStatisticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_reproductions_farm_start_date",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "ix_reproductions_farm_status_end_date",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfer_requests_destination_status_updated_at",
                schema: "app",
                table: "internal_transfer_requests",
                columns: new[] { "DestinationBreedingFarmId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfer_requests_source_status_updated_at",
                schema: "app",
                table: "internal_transfer_requests",
                columns: new[] { "SourceBreedingFarmId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_external_transfers_farm_created_at",
                schema: "app",
                table: "external_transfers",
                columns: new[] { "BreedingFarmId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_birds_farm_birth_date",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "BirthDate" },
                filter: "\"BirthDate\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_birds_farm_created_at",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reproductions_farm_start_date",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropIndex(
                name: "ix_reproductions_farm_status_end_date",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropIndex(
                name: "ix_internal_transfer_requests_destination_status_updated_at",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropIndex(
                name: "ix_internal_transfer_requests_source_status_updated_at",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropIndex(
                name: "ix_external_transfers_farm_created_at",
                schema: "app",
                table: "external_transfers");

            migrationBuilder.DropIndex(
                name: "ix_birds_farm_birth_date",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "ix_birds_farm_created_at",
                schema: "app",
                table: "birds");
        }
    }
}
