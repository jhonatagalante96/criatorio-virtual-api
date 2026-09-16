using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmVisualIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityContentType",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityFileName",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VisualIdentityLength",
                schema: "app",
                table: "breeding_farms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualIdentityReference",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisualIdentitySource",
                schema: "app",
                table: "breeding_farms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL) OR (\"VisualIdentitySource\" = 1 AND \"VisualIdentityFileName\" IS NOT NULL AND btrim(\"VisualIdentityFileName\") <> '' AND \"VisualIdentityContentType\" IN ('image/jpeg', 'image/png') AND \"VisualIdentityLength\" > 0 AND \"VisualIdentityLength\" <= 10485760) OR (\"VisualIdentitySource\" = 2 AND \"VisualIdentityFileName\" IS NULL AND \"VisualIdentityContentType\" IS NULL AND \"VisualIdentityLength\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_visual_identity_reference_source_pair",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"VisualIdentitySource\" IS NULL AND \"VisualIdentityReference\" IS NULL) OR (\"VisualIdentitySource\" IN (1, 2) AND \"VisualIdentityReference\" IS NOT NULL AND btrim(\"VisualIdentityReference\") <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_visual_identity_metadata",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_visual_identity_reference_source_pair",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityContentType",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityFileName",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityLength",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentityReference",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "VisualIdentitySource",
                schema: "app",
                table: "breeding_farms");
        }
    }
}
