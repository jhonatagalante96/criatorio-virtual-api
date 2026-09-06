using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBreedingFarms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "breeding_farms",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResponsibleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OfficialRegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AddressNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AddressComplement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressNeighborhood = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AddressState = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AddressPostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_breeding_farms", x => x.Id);
                    table.CheckConstraint("ck_breeding_farms_name_not_blank", "btrim(\"Name\") <> ''");
                    table.CheckConstraint("ck_breeding_farms_official_registration_not_blank", "\"OfficialRegistrationNumber\" IS NULL OR btrim(\"OfficialRegistrationNumber\") <> ''");
                });

            migrationBuilder.CreateTable(
                name: "breeding_farm_users",
                schema: "app",
                columns: table => new
                {
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_breeding_farm_users", x => new { x.BreedingFarmId, x.UserId });
                    table.ForeignKey(
                        name: "FK_breeding_farm_users_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_breeding_farm_users_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_breeding_farm_users_UserId",
                schema: "app",
                table: "breeding_farm_users",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ux_breeding_farm_users_active_owner",
                schema: "app",
                table: "breeding_farm_users",
                column: "BreedingFarmId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"Role\" = 1");

            migrationBuilder.CreateIndex(
                name: "ux_breeding_farms_official_registration_number",
                schema: "app",
                table: "breeding_farms",
                column: "OfficialRegistrationNumber",
                unique: true,
                filter: "\"OfficialRegistrationNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "breeding_farm_users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "breeding_farms",
                schema: "app");
        }
    }
}
