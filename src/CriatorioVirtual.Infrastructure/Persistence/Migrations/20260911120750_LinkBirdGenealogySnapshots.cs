using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkBirdGenealogySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_genealogy_nodes_bird_root",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_genealogy_nodes_root",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.AddColumn<Guid>(
                name: "BreedingFarmId",
                schema: "app",
                table: "genealogy_nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GenealogyRootId",
                schema: "app",
                table: "genealogy_nodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Position",
                schema: "app",
                table: "genealogy_nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "SnapshotBirthDate",
                schema: "app",
                table: "genealogy_nodes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotName",
                schema: "app",
                table: "genealogy_nodes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotRingNumber",
                schema: "app",
                table: "genealogy_nodes",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotSex",
                schema: "app",
                table: "genealogy_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotStatus",
                schema: "app",
                table: "genealogy_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE app.genealogy_nodes AS node
                SET "BreedingFarmId" = bird."BreedingFarmId",
                    "GenealogyRootId" = node."Id",
                    "LinkedBirdId" = node."BirdId",
                    "Position" = 'root'
                FROM app.birds AS bird
                WHERE bird."Id" = node."BirdId";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO app.genealogy_nodes
                    ("Id", "BreedingFarmId", "BirdId", "GenealogyRootId", "Position", "LinkedBirdId",
                     "SnapshotName", "SnapshotSex", "SnapshotBirthDate", "SnapshotRingNumber", "SnapshotStatus",
                     "IsRoot", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), child."BreedingFarmId", parent."Id", root."Id", 'father', parent."Id",
                       parent."Name", parent."Sex", parent."BirthDate", parent."RingNumber", parent."Status",
                       FALSE, root."CreatedAtUtc", root."UpdatedAtUtc"
                FROM app.birds AS child
                INNER JOIN app.genealogy_nodes AS root
                    ON root."BirdId" = child."Id" AND root."IsRoot" = TRUE
                INNER JOIN app.birds AS parent
                    ON parent."Id" = child."FatherBirdId"
                    AND parent."BreedingFarmId" = child."BreedingFarmId"
                UNION ALL
                SELECT gen_random_uuid(), child."BreedingFarmId", parent."Id", root."Id", 'mother', parent."Id",
                       parent."Name", parent."Sex", parent."BirthDate", parent."RingNumber", parent."Status",
                       FALSE, root."CreatedAtUtc", root."UpdatedAtUtc"
                FROM app.birds AS child
                INNER JOIN app.genealogy_nodes AS root
                    ON root."BirdId" = child."Id" AND root."IsRoot" = TRUE
                INNER JOIN app.birds AS parent
                    ON parent."Id" = child."MotherBirdId"
                    AND parent."BreedingFarmId" = child."BreedingFarmId";
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_birds_farm_id",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_genealogy_nodes_BreedingFarmId_LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "LinkedBirdId" });

            migrationBuilder.CreateIndex(
                name: "ux_genealogy_nodes_bird_root",
                schema: "app",
                table: "genealogy_nodes",
                column: "BirdId",
                unique: true,
                filter: "\"IsRoot\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ux_genealogy_nodes_root_position",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "GenealogyRootId", "Position" },
                unique: true);

                migrationBuilder.AddCheckConstraint(
                name: "ck_genealogy_nodes_root_position",
                schema: "app",
                table: "genealogy_nodes",
                sql: "(\"IsRoot\" = TRUE AND \"Position\" = 'root' AND \"LinkedBirdId\" = \"BirdId\") OR (\"IsRoot\" = FALSE AND \"BreedingFarmId\" IS NOT NULL AND \"Position\" <> 'root' AND \"LinkedBirdId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_genealogy_nodes_snapshot_required",
                schema: "app",
                table: "genealogy_nodes",
                sql: "\"IsRoot\" = TRUE OR (\"SnapshotName\" IS NOT NULL AND \"SnapshotSex\" IS NOT NULL AND \"SnapshotStatus\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_genealogy_nodes_birds_BreedingFarmId_LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes",
                columns: new[] { "BreedingFarmId", "LinkedBirdId" },
                principalSchema: "app",
                principalTable: "birds",
                principalColumns: new[] { "BreedingFarmId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_genealogy_nodes_genealogy_nodes_GenealogyRootId",
                schema: "app",
                table: "genealogy_nodes",
                column: "GenealogyRootId",
                principalSchema: "app",
                principalTable: "genealogy_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM app.genealogy_nodes WHERE \"IsRoot\" = FALSE;");

            migrationBuilder.DropForeignKey(
                name: "FK_genealogy_nodes_birds_BreedingFarmId_LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropForeignKey(
                name: "FK_genealogy_nodes_genealogy_nodes_GenealogyRootId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "IX_genealogy_nodes_BreedingFarmId_LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "ux_genealogy_nodes_bird_root",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropIndex(
                name: "ux_genealogy_nodes_root_position",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_genealogy_nodes_root_position",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_genealogy_nodes_snapshot_required",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_birds_farm_id",
                schema: "app",
                table: "birds");

            migrationBuilder.DropColumn(
                name: "BreedingFarmId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "GenealogyRootId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "LinkedBirdId",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotBirthDate",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotName",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotRingNumber",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotSex",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotStatus",
                schema: "app",
                table: "genealogy_nodes");

            migrationBuilder.CreateIndex(
                name: "ux_genealogy_nodes_bird_root",
                schema: "app",
                table: "genealogy_nodes",
                column: "BirdId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_genealogy_nodes_root",
                schema: "app",
                table: "genealogy_nodes",
                sql: "\"IsRoot\" = TRUE");
        }
    }
}
