using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateInternalTransferRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "internal_transfer_requests",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_transfer_requests", x => x.Id);
                    table.CheckConstraint("ck_internal_transfer_requests_distinct_farms", "\"SourceBreedingFarmId\" <> \"DestinationBreedingFarmId\"");
                    table.CheckConstraint("ck_internal_transfer_requests_status_valid", "\"Status\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_internal_transfer_requests_birds_BirdId",
                        column: x => x.BirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_internal_transfer_requests_breeding_farms_DestinationBreedi~",
                        column: x => x.DestinationBreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_internal_transfer_requests_breeding_farms_SourceBreedingFar~",
                        column: x => x.SourceBreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_internal_transfer_requests_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfer_requests_destination_status_created_at",
                schema: "app",
                table: "internal_transfer_requests",
                columns: new[] { "DestinationBreedingFarmId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_internal_transfer_requests_RequestedByUserId",
                schema: "app",
                table: "internal_transfer_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_internal_transfer_requests_source_status_created_at",
                schema: "app",
                table: "internal_transfer_requests",
                columns: new[] { "SourceBreedingFarmId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_internal_transfer_requests_bird_pending",
                schema: "app",
                table: "internal_transfer_requests",
                column: "BirdId",
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_transfer_requests",
                schema: "app");
        }
    }
}
