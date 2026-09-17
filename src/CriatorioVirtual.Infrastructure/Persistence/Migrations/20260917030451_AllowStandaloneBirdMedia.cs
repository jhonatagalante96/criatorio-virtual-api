using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowStandaloneBirdMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_birds_bird_attachments_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "IX_birds_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_bird_attachments_farm_bird_id",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.AlterColumn<Guid>(
                name: "BirdId",
                schema: "app",
                table: "bird_attachments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM app.bird_attachments WHERE "BirdId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot make BirdId required while standalone media exists.';
                    END IF;
                END $$;
                """);

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

            migrationBuilder.AlterColumn<Guid>(
                name: "BirdId",
                schema: "app",
                table: "bird_attachments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_bird_attachments_farm_bird_id",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "BirdId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_birds_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "Id", "PrimaryPhotoId" });

            migrationBuilder.AddForeignKey(
                name: "FK_birds_bird_attachments_BreedingFarmId_Id_PrimaryPhotoId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "Id", "PrimaryPhotoId" },
                principalSchema: "app",
                principalTable: "bird_attachments",
                principalColumns: new[] { "BreedingFarmId", "BirdId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
