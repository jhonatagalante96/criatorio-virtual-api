using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateExternalTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_transfers",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_transfers", x => x.Id);
                    table.CheckConstraint("ck_external_transfers_recipient_name_not_blank", "btrim(\"RecipientName\") <> ''");
                    table.ForeignKey(
                        name: "FK_external_transfers_birds_BreedingFarmId_BirdId",
                        columns: x => new { x.BreedingFarmId, x.BirdId },
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumns: new[] { "BreedingFarmId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_external_transfers_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_transfers_BreedingFarmId_BirdId",
                schema: "app",
                table: "external_transfers",
                columns: new[] { "BreedingFarmId", "BirdId" });

            migrationBuilder.CreateIndex(
                name: "ux_external_transfers_bird",
                schema: "app",
                table: "external_transfers",
                column: "BirdId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_transfers",
                schema: "app");
        }
    }
}
