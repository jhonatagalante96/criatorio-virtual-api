using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveReproductionHistoryAcrossTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reproductions_birds_BreedingFarmId_FemaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropForeignKey(
                name: "FK_reproductions_birds_BreedingFarmId_MaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropIndex(
                name: "IX_reproductions_BreedingFarmId_FemaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.CreateIndex(
                name: "IX_reproductions_FemaleBirdId",
                schema: "app",
                table: "reproductions",
                column: "FemaleBirdId");

            migrationBuilder.CreateIndex(
                name: "IX_reproductions_MaleBirdId",
                schema: "app",
                table: "reproductions",
                column: "MaleBirdId");

            migrationBuilder.AddForeignKey(
                name: "FK_reproductions_birds_FemaleBirdId",
                schema: "app",
                table: "reproductions",
                column: "FemaleBirdId",
                principalSchema: "app",
                principalTable: "birds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reproductions_birds_MaleBirdId",
                schema: "app",
                table: "reproductions",
                column: "MaleBirdId",
                principalSchema: "app",
                principalTable: "birds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reproductions_birds_FemaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropForeignKey(
                name: "FK_reproductions_birds_MaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropIndex(
                name: "IX_reproductions_FemaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropIndex(
                name: "IX_reproductions_MaleBirdId",
                schema: "app",
                table: "reproductions");

            migrationBuilder.CreateIndex(
                name: "IX_reproductions_BreedingFarmId_FemaleBirdId",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "FemaleBirdId" });

            migrationBuilder.AddForeignKey(
                name: "FK_reproductions_birds_BreedingFarmId_FemaleBirdId",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "FemaleBirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reproductions_birds_BreedingFarmId_MaleBirdId",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "MaleBirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
