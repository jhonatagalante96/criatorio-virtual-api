using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReproductionParticipantHistoricalSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FemaleBirdBirthDate",
                schema: "app",
                table: "reproductions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FemaleBirdName",
                schema: "app",
                table: "reproductions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "FemaleBirdRingNumber",
                schema: "app",
                table: "reproductions",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FemaleBirdSex",
                schema: "app",
                table: "reproductions",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "FemaleBirdStatus",
                schema: "app",
                table: "reproductions",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MaleBirdBirthDate",
                schema: "app",
                table: "reproductions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaleBirdName",
                schema: "app",
                table: "reproductions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "MaleBirdRingNumber",
                schema: "app",
                table: "reproductions",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaleBirdSex",
                schema: "app",
                table: "reproductions",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "MaleBirdStatus",
                schema: "app",
                table: "reproductions",
                type: "integer",
                nullable: false);

            migrationBuilder.Sql(
                """
                UPDATE app.reproductions r
                SET
                    "MaleBirdName" = mb."Name",
                    "MaleBirdSex" = mb."Sex",
                    "MaleBirdBirthDate" = mb."BirthDate",
                    "MaleBirdRingNumber" = mb."RingNumber",
                    "MaleBirdStatus" = mb."Status",
                    "FemaleBirdName" = fb."Name",
                    "FemaleBirdSex" = fb."Sex",
                    "FemaleBirdBirthDate" = fb."BirthDate",
                    "FemaleBirdRingNumber" = fb."RingNumber",
                    "FemaleBirdStatus" = fb."Status"
                FROM app.birds mb, app.birds fb
                WHERE r."MaleBirdId" = mb."Id"
                  AND r."FemaleBirdId" = fb."Id";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reproductions_female_snapshot_sex",
                schema: "app",
                table: "reproductions",
                sql: "\"FemaleBirdSex\" IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reproductions_female_snapshot_status",
                schema: "app",
                table: "reproductions",
                sql: "\"FemaleBirdStatus\" IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reproductions_male_snapshot_sex",
                schema: "app",
                table: "reproductions",
                sql: "\"MaleBirdSex\" IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reproductions_male_snapshot_status",
                schema: "app",
                table: "reproductions",
                sql: "\"MaleBirdStatus\" IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_reproductions_female_snapshot_sex",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reproductions_female_snapshot_status",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reproductions_male_snapshot_sex",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reproductions_male_snapshot_status",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "FemaleBirdBirthDate",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "FemaleBirdName",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "FemaleBirdRingNumber",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "FemaleBirdSex",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "FemaleBirdStatus",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "MaleBirdBirthDate",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "MaleBirdName",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "MaleBirdRingNumber",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "MaleBirdSex",
                schema: "app",
                table: "reproductions");

            migrationBuilder.DropColumn(
                name: "MaleBirdStatus",
                schema: "app",
                table: "reproductions");
        }
    }
}
