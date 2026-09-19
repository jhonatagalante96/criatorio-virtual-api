using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalTransferHistoricalSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BirdSnapshotName",
                schema: "app",
                table: "internal_transfer_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BirdSnapshotRingNumber",
                schema: "app",
                table: "internal_transfer_requests",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirdSnapshotSex",
                schema: "app",
                table: "internal_transfer_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirdSnapshotStatus",
                schema: "app",
                table: "internal_transfer_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE app.internal_transfer_requests t
                SET "BirdSnapshotName" = COALESCE(NULLIF(btrim(b."Name"), ''), 'Ave transferida'),
                    "BirdSnapshotSex" = CASE WHEN b."Sex" IN (1, 2, 3) THEN b."Sex" ELSE 3 END,
                    "BirdSnapshotRingNumber" = b."RingNumber",
                    "BirdSnapshotStatus" = 1
                FROM app.birds b
                WHERE t."BirdId" = b."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "BirdSnapshotName",
                schema: "app",
                table: "internal_transfer_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false);

            migrationBuilder.AlterColumn<int>(
                name: "BirdSnapshotSex",
                schema: "app",
                table: "internal_transfer_requests",
                type: "integer",
                nullable: false);

            migrationBuilder.AlterColumn<int>(
                name: "BirdSnapshotStatus",
                schema: "app",
                table: "internal_transfer_requests",
                type: "integer",
                nullable: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_name_not_blank",
                schema: "app",
                table: "internal_transfer_requests",
                sql: "btrim(\"BirdSnapshotName\") <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_ring_number_format",
                schema: "app",
                table: "internal_transfer_requests",
                sql: "\"BirdSnapshotRingNumber\" IS NULL OR \"BirdSnapshotRingNumber\" ~ '^[0-9]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_sex_valid",
                schema: "app",
                table: "internal_transfer_requests",
                sql: "\"BirdSnapshotSex\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_status_valid",
                schema: "app",
                table: "internal_transfer_requests",
                sql: "\"BirdSnapshotStatus\" IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_name_not_blank",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_ring_number_format",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_sex_valid",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_internal_transfer_requests_bird_snapshot_status_valid",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropColumn(
                name: "BirdSnapshotName",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropColumn(
                name: "BirdSnapshotRingNumber",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropColumn(
                name: "BirdSnapshotSex",
                schema: "app",
                table: "internal_transfer_requests");

            migrationBuilder.DropColumn(
                name: "BirdSnapshotStatus",
                schema: "app",
                table: "internal_transfer_requests");
        }
    }
}
