using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegisterReproduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reproductions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaleBirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    FemaleBirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reproductions", x => x.Id);
                    table.CheckConstraint("ck_reproductions_date_range", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("ck_reproductions_distinct_birds", "\"MaleBirdId\" <> \"FemaleBirdId\"");
                    table.CheckConstraint("ck_reproductions_status_valid", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_reproductions_birds_BreedingFarmId_FemaleBirdId",
                        columns: x => new { x.BreedingFarmId, x.FemaleBirdId },
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reproductions_birds_BreedingFarmId_MaleBirdId",
                        columns: x => new { x.BreedingFarmId, x.MaleBirdId },
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reproductions_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reproductions_BreedingFarmId_FemaleBirdId",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "FemaleBirdId" });

            migrationBuilder.CreateIndex(
                name: "ix_reproductions_farm_pair",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "MaleBirdId", "FemaleBirdId" });

            migrationBuilder.CreateIndex(
                name: "ix_reproductions_farm_status_start_date",
                schema: "app",
                table: "reproductions",
                columns: new[] { "BreedingFarmId", "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reproductions",
                schema: "app");
        }
    }
}
