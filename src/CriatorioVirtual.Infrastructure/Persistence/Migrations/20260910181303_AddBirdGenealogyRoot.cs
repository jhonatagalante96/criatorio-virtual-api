using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBirdGenealogyRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "app",
                table: "birds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeathDate",
                schema: "app",
                table: "birds",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "genealogy_nodes",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsRoot = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_genealogy_nodes", x => x.Id);
                    table.CheckConstraint("ck_genealogy_nodes_root", "\"IsRoot\" = TRUE");
                    table.ForeignKey(
                        name: "FK_genealogy_nodes_birds_BirdId",
                        column: x => x.BirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_birds_death_date_after_birth_date",
                schema: "app",
                table: "birds",
                sql: "\"DeathDate\" IS NULL OR \"BirthDate\" IS NULL OR \"DeathDate\" >= \"BirthDate\"");

            migrationBuilder.CreateIndex(
                name: "ux_genealogy_nodes_bird_root",
                schema: "app",
                table: "genealogy_nodes",
                column: "BirdId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "genealogy_nodes",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_birds_death_date_after_birth_date",
                schema: "app",
                table: "birds");

            migrationBuilder.DropColumn(
                name: "DeathDate",
                schema: "app",
                table: "birds");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "app",
                table: "birds",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
