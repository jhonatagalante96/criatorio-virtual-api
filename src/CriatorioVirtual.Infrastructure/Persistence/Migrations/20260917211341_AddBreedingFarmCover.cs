using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriatorioVirtual.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreedingFarmCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverContentType",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverFileName",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CoverLength",
                schema: "app",
                table: "breeding_farms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverReference",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CoverSource",
                schema: "app",
                table: "breeding_farms",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverTemplateConfiguration",
                schema: "app",
                table: "breeding_farms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverTemplateModelId",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverTemplateVersion",
                schema: "app",
                table: "breeding_farms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CoverUpdatedAtUtc",
                schema: "app",
                table: "breeding_farms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_cover_metadata",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"CoverSource\" IS NULL AND \"CoverFileName\" IS NULL AND \"CoverContentType\" IS NULL AND \"CoverLength\" IS NULL AND \"CoverTemplateModelId\" IS NULL AND \"CoverTemplateVersion\" IS NULL AND \"CoverTemplateConfiguration\" IS NULL AND \"CoverUpdatedAtUtc\" IS NULL) OR (\"CoverSource\" = 1 AND \"CoverFileName\" IS NOT NULL AND btrim(\"CoverFileName\") <> '' AND \"CoverContentType\" = 'image/png' AND \"CoverLength\" > 0 AND \"CoverLength\" <= 20971520 AND \"CoverTemplateModelId\" IS NULL AND \"CoverTemplateVersion\" IS NULL AND \"CoverTemplateConfiguration\" IS NULL AND \"CoverUpdatedAtUtc\" IS NOT NULL) OR (\"CoverSource\" = 2 AND \"CoverFileName\" IS NOT NULL AND btrim(\"CoverFileName\") <> '' AND \"CoverContentType\" = 'image/png' AND \"CoverLength\" > 0 AND \"CoverLength\" <= 20971520 AND \"CoverTemplateModelId\" IS NOT NULL AND btrim(\"CoverTemplateModelId\") <> '' AND \"CoverTemplateVersion\" IS NOT NULL AND btrim(\"CoverTemplateVersion\") <> '' AND \"CoverTemplateConfiguration\" IS NOT NULL AND jsonb_typeof(\"CoverTemplateConfiguration\") = 'object' AND \"CoverUpdatedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_breeding_farms_cover_reference_source_pair",
                schema: "app",
                table: "breeding_farms",
                sql: "(\"CoverSource\" IS NULL AND \"CoverReference\" IS NULL) OR (\"CoverSource\" IS NOT NULL AND \"CoverSource\" IN (1, 2) AND \"CoverReference\" IS NOT NULL AND btrim(\"CoverReference\") <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_cover_metadata",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_breeding_farms_cover_reference_source_pair",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverContentType",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverFileName",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverLength",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverReference",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverSource",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverTemplateConfiguration",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverTemplateModelId",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverTemplateVersion",
                schema: "app",
                table: "breeding_farms");

            migrationBuilder.DropColumn(
                name: "CoverUpdatedAtUtc",
                schema: "app",
                table: "breeding_farms");
        }
    }
}
