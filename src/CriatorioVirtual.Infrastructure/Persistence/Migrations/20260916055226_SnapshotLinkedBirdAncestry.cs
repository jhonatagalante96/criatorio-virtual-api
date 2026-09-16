using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotLinkedBirdAncestry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanNavigateToSourceBird",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBirdSnapshot",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SnapshotBirthDate",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotRingNumber",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SnapshotSourceBirdId",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotStatus",
                schema: "app",
                table: "external_genealogy_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_consistency",
                schema: "app",
                table: "external_genealogy_nodes",
                sql: "(\"IsBirdSnapshot\" = FALSE AND \"SnapshotSourceBirdId\" IS NULL AND \"SnapshotBirthDate\" IS NULL AND \"SnapshotRingNumber\" IS NULL AND \"SnapshotStatus\" IS NULL AND \"CanNavigateToSourceBird\" = FALSE) OR (\"IsBirdSnapshot\" = TRUE AND \"SnapshotStatus\" IS NOT NULL AND (\"CanNavigateToSourceBird\" = FALSE OR \"SnapshotSourceBirdId\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_ring_number_format",
                schema: "app",
                table: "external_genealogy_nodes",
                sql: "\"SnapshotRingNumber\" IS NULL OR \"SnapshotRingNumber\" ~ '^[0-9]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_status_valid",
                schema: "app",
                table: "external_genealogy_nodes",
                sql: "\"SnapshotStatus\" IS NULL OR \"SnapshotStatus\" IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_consistency",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_ring_number_format",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_external_genealogy_nodes_snapshot_status_valid",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "CanNavigateToSourceBird",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "IsBirdSnapshot",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotBirthDate",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotRingNumber",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotSourceBirdId",
                schema: "app",
                table: "external_genealogy_nodes");

            migrationBuilder.DropColumn(
                name: "SnapshotStatus",
                schema: "app",
                table: "external_genealogy_nodes");
        }
    }
}
