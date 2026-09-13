using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBirdCompetitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bird_competitions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompetitionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Placement = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bird_competitions", x => x.Id);
                    table.CheckConstraint("ck_bird_competitions_name_not_blank", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_bird_competitions_placement_positive", "\"Placement\" IS NULL OR \"Placement\" > 0");
                    table.ForeignKey(
                        name: "FK_bird_competitions_birds_BirdId",
                        column: x => x.BirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bird_competitions_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bird_competitions_BirdId",
                schema: "app",
                table: "bird_competitions",
                column: "BirdId");

            migrationBuilder.CreateIndex(
                name: "ix_bird_competitions_farm_bird_date_created_at",
                schema: "app",
                table: "bird_competitions",
                columns: new[] { "BreedingFarmId", "BirdId", "CompetitionDate", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bird_competitions",
                schema: "app");
        }
    }
}
