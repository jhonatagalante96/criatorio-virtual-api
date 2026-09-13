using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBirdAttachmentRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bird_attachments_farm_bird_created_at",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                schema: "app",
                table: "bird_attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StorageCleanupPending",
                schema: "app",
                table: "bird_attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_bird_attachments_farm_bird_created_at",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "BirdId", "DeletedAtUtc", "CreatedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_bird_attachments_cleanup_requires_deletion",
                schema: "app",
                table: "bird_attachments",
                sql: "\"StorageCleanupPending\" = FALSE OR \"DeletedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bird_attachments_farm_bird_created_at",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bird_attachments_cleanup_requires_deletion",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.DropColumn(
                name: "StorageCleanupPending",
                schema: "app",
                table: "bird_attachments");

            migrationBuilder.CreateIndex(
                name: "ix_bird_attachments_farm_bird_created_at",
                schema: "app",
                table: "bird_attachments",
                columns: new[] { "BreedingFarmId", "BirdId", "CreatedAtUtc" });
        }
    }
}
