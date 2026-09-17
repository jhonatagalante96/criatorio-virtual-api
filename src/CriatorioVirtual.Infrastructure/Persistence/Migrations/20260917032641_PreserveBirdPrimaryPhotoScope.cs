using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveBirdPrimaryPhotoScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_birds_bird_attachments_BreedingFarmId_PrimaryPhotoId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "IX_birds_BreedingFarmId_PrimaryPhotoId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_bird_attachments_farm_id",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.CreateIndex(
                name: "IX_birds_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "Id", "PrimaryPhotoId" });

            migrationBuilder.CreateIndex(
                name: "ux_bird_attachments_farm_bird_id",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "BirdId", "Id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE app.birds
                ADD CONSTRAINT "FK_birds_bird_attachments_BreedingFarmId_Id_PrimaryPhotoId"
                FOREIGN KEY ("BreedingFarmId", "Id", "PrimaryPhotoId")
                REFERENCES app.bird_attachments ("BreedingFarmId", "BirdId", "Id")
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE app.birds
                DROP CONSTRAINT "FK_birds_bird_attachments_BreedingFarmId_Id_PrimaryPhotoId";
                """);

            migrationBuilder.DropIndex(
                name: "IX_birds_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "ux_bird_attachments_farm_bird_id",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_bird_attachments_farm_id",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_birds_BreedingFarmId_PrimaryPhotoId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "PrimaryPhotoId" });

            migrationBuilder.AddForeignKey(
                name: "FK_birds_bird_attachments_BreedingFarmId_PrimaryPhotoId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "PrimaryPhotoId" },
                principalSchema: "app",
                principalTable: "bird_attachments",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
