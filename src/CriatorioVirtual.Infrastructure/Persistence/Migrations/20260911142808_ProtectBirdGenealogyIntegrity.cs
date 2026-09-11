using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectBirdGenealogyIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_birds_birds_FatherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropForeignKey(
                name: "FK_birds_birds_MotherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropForeignKey(
                name: "FK_genealogy_nodes_birds_BirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "IX_birds_FatherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "IX_birds_MotherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.Sql(
                """
                UPDATE app.genealogy_nodes AS node
                SET "BreedingFarmId" = bird."BreedingFarmId"
                FROM app.birds AS bird
                WHERE node."BirdId" = bird."Id"
                  AND node."BreedingFarmId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "BreedingFarmId",
                schema: "app",
                table: "genealogy_nodes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_genealogy_nodes_BreedingFarmId_BirdId",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "BirdId" });

            migrationBuilder.CreateIndex(
                name: "IX_birds_BreedingFarmId_FatherBirdId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "FatherBirdId" });

            migrationBuilder.CreateIndex(
                name: "IX_birds_BreedingFarmId_MotherBirdId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "MotherBirdId" });

            migrationBuilder.AddForeignKey(
                name: "FK_birds_birds_BreedingFarmId_FatherBirdId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "FatherBirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_birds_birds_BreedingFarmId_MotherBirdId",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "MotherBirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_genealogy_nodes_birds_BreedingFarmId_BirdId",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "BirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_birds_birds_BreedingFarmId_FatherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropForeignKey(
                name: "FK_birds_birds_BreedingFarmId_MotherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropForeignKey(
                name: "FK_genealogy_nodes_birds_BreedingFarmId_BirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "IX_genealogy_nodes_BreedingFarmId_BirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "IX_birds_BreedingFarmId_FatherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.DropIndex(
                name: "IX_birds_BreedingFarmId_MotherBirdId",
                schema: "app",
                table: "birds");

            migrationBuilder.AlterColumn<Guid>(
                name: "BreedingFarmId",
                schema: "app",
                table: "genealogy_nodes",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_birds_FatherBirdId",
                schema: "app",
                table: "birds",
                column: "FatherBirdId");

            migrationBuilder.CreateIndex(
                name: "IX_birds_MotherBirdId",
                schema: "app",
                table: "birds",
                column: "MotherBirdId");

            migrationBuilder.AddForeignKey(
                name: "FK_birds_birds_FatherBirdId",
                schema: "app",
                table: "birds",
                column: "FatherBirdId",
                principalSchema: "app",
                principalTable: "birds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_birds_birds_MotherBirdId",
                schema: "app",
                table: "birds",
                column: "MotherBirdId",
                principalSchema: "app",
                principalTable: "birds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_genealogy_nodes_birds_BirdId",
                schema: "app",
                table: "genealogy_nodes",
                column: "BirdId",
                principalSchema: "app",
                principalTable: "birds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
