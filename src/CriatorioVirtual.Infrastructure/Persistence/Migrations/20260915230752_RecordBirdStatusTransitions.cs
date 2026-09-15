using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordBirdStatusTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bird_status_transitions",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BirdId = table.Column<Guid>(type: "uuid", nullable: false),
                    BreedingFarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bird_status_transitions", x => x.Id);
                    table.CheckConstraint("ck_bird_status_transitions_from_status_valid", "\"FromStatus\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_bird_status_transitions_status_changed", "\"FromStatus\" <> \"ToStatus\"");
                    table.CheckConstraint("ck_bird_status_transitions_to_status_valid", "\"ToStatus\" IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_bird_status_transitions_birds_BirdId",
                        column: x => x.BirdId,
                        principalSchema: "app",
                        principalTable: "birds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bird_status_transitions_breeding_farms_BreedingFarmId",
                        column: x => x.BreedingFarmId,
                        principalSchema: "app",
                        principalTable: "breeding_farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bird_status_transitions_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bird_status_transitions_bird_created_at",
                schema: "app",
                table: "bird_status_transitions",
                columns: new[] { "BirdId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_bird_status_transitions_ChangedByUserId",
                schema: "app",
                table: "bird_status_transitions",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_bird_status_transitions_farm_created_at",
                schema: "app",
                table: "bird_status_transitions",
                columns: new[] { "BreedingFarmId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bird_status_transitions",
                schema: "app");
        }
    }
}
