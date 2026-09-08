using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBirds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "birds",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sex = table.Column<int>(type: "integer", nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RingNumber = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    FatherBirdId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalFatherName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MotherBirdId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalMotherName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_birds", x => x.Id);
                    table.CheckConstraint("ck_birds_father_source_exclusive", "\"FatherBirdId\" IS NULL OR \"ExternalFatherName\" IS NULL");
                    table.CheckConstraint("ck_birds_mother_source_exclusive", "\"MotherBirdId\" IS NULL OR \"ExternalMotherName\" IS NULL");
                    table.CheckConstraint("ck_birds_name_not_blank", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_birds_ring_number_format", "\"RingNumber\" IS NULL OR \"RingNumber\" ~ '^[0-9]{6}$'");
                    table.CheckConstraint("ck_birds_sex_valid", "\"Sex\" IN (1, 2, 3)");
                    table.CheckConstraint("ck_birds_status_valid", "\"Status\" IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_birds_birds_FatherBirdId",
                        column: x => x.FatherBirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_birds_birds_MotherBirdId",
                        column: x => x.MotherBirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_birds_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_birds_species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalSchema: "app",
                        principalTable: "species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_birds_farm_status",
                schema: "app",
                table: "birds",
                columns: new[] { "BreedingFarmId", "Status" });

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

            migrationBuilder.CreateIndex(
                name: "IX_birds_SpeciesId",
                schema: "app",
                table: "birds",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "ux_birds_ring_number",
                schema: "app",
                table: "birds",
                column: "RingNumber",
                unique: true,
                filter: "\"RingNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "birds",
                schema: "app");
        }
    }
}
